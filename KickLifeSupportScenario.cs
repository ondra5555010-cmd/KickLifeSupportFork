using JetBrains.Annotations;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace KickLifeSupport
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class KickLifeSupportScenario : ScenarioModule
    {
        const double epsilon = 0.00000001;
        public static double AmbientPressureMinimumKPa { get; private set; } = 50.0;
        public static double OpenLoopELSPressureMinimumKPa { get; private set; } = 5.0;

        public static KickLifeSupportScenario Instance { get; private set; }
        KickLifeSupportSettings gameSettings;

        public double GraceOxygen => graceO2;
        public double GraceWater => graceWater;
        public double GraceFood => graceFood;
        public double GraceClimate => graceClimate;
        public double GraceTemp => graceTemp;
        public double CO2WarningLevel => co2Warning;
        public double CO2FatalLevel => co2Fatal;

        public Dictionary<Guid, LifeSupportStatus> database = new Dictionary<Guid, LifeSupportStatus>();

        class VesselLifeSupportContext
        {
            public Vessel vessel;
            public bool loaded;
            public int liveCrew;
            public int pressurizedCrew;
            public int ambientDependentCrew;
            public int cabinCapacity;
            public float cabinCO2;
            public bool ambientSafe;
            public bool underwater;
            public double occupancyScale;
            public readonly List<KickLifeSupportModule> lifeSupportModules = new List<KickLifeSupportModule>();
            public readonly List<KickTemperatureControlModule> temperatureModules = new List<KickTemperatureControlModule>();
            public readonly List<ProtoLifeSupportPart> protoLifeSupportParts = new List<ProtoLifeSupportPart>();
            public readonly List<ProtoTemperaturePart> protoTemperatureParts = new List<ProtoTemperaturePart>();
            public readonly List<ScrubberContribution> scrubberContributions = new List<ScrubberContribution>();
        }

        class ProtoLifeSupportPart
        {
            public ProtoPartSnapshot part;
            public ProtoPartModuleSnapshot module;
            public int crew;
            public int capacity;
            public int atmosphereControlMode;
            public bool atmosphericControlEnabled;
        }

        class ProtoTemperaturePart
        {
            public ProtoPartSnapshot part;
            public ProtoPartModuleSnapshot module;
            public int crew;
            public int capacity;
            public bool enabled;
            public double ecRate;
        }

        struct ScrubberContribution
        {
            public KickLifeSupportModule module;
            public ProtoLifeSupportPart protoPart;
            public int mode;
            public int capacity;
            public bool loaded;
            public bool usesOpenLoopOxygenAssist;
        }

        /// <summary>
        /// The total amount of air in liters per seat
        /// </summary>
        const double airPerSeat = 2000;
        /// <summary>
        /// The CO2 concentration (in liters) threshold for warning. This is 3% of airPerKerbal.
        /// </summary>
        const double co2Warning = 0.03;
        /// <summary>
        /// The CO2 concentration (in liters) threshold for fatality. This is 10% of airPerKerbal.
        /// </summary>
        const double co2Fatal = 0.1;
        #region Resource IDs
        int o2Id = -1;
        int co2Id = -1;
        int foodId = -1;
        int waterId = -1;
        int wasteId = -1;
        int wasteWaterId = -1;
        int lithiumHydroxideId = -1;
        int electricChargeId = -1;
        #endregion

        #region Rates
        // Resource Rates
        float o2RequestRate;
        float co2RequestRate;
        float scrubberRequestRate;
        float foodRequestRate;
        float waterRequestRate;
        float wasteRequestRate;
        float wasteWaterRequestRate;
        float lithiumHydroxideRequestRate;
        float regenerativeScrubberECRequestRate;

        // EC Rates
        float scrubberECRequestRate;
        float openLoopELSECRequestRate;
        float openLoopELSOxygenMultiplier;

        // Grace Periods
        float graceO2;
        float graceWater;
        float graceFood;
        float graceClimate;
        float graceTemp;
        float ambientPressureMinimum;
        float openLoopELSPressureMinimum;

        // Heat Generation
        public float kerbalHeat;

        const double minSafeTemp = 5;   // 5c
        const double maxSafeTemp = 45;  // 45c

        #endregion

        public double lastScrubAmount = 0;

        public override void OnAwake()
        {
            Instance = this;
            Debug.Log("[KICKLS] Scenario Module Awake");

            GetSettings();
            GetResourceIds();

            Debug.Log($"[KICKLS] O2 ID: {o2Id} | Rate: {o2RequestRate}");
            Debug.Log($"[KICKLS] Water ID: {waterId} | Rate: {waterRequestRate}");
            Debug.Log($"[KICKLS] Food ID: {foodId} | Rate: {foodRequestRate}");

            if (o2Id == -1) Debug.LogError("[KICKLS] CRITICAL: Oxygen Resource ID not found!");
            if (o2RequestRate <= 0) Debug.LogError("[KICKLS] CRITICAL: Oxygen Rate is 0!");
        }

        public void FixedUpdate()
        {
            double currentTime = Planetarium.GetUniversalTime();

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                VesselLifeSupportContext ctx = BuildContext(v);
                if (ctx == null) continue;

                LifeSupportStatus data = GetData(v.id);

                // Initialize
                if (data.lastUpdateTime == 0)
                {
                    data.lastUpdateTime = currentTime;
                    continue;
                }

                double deltaTime = currentTime - data.lastUpdateTime;

                if (!v.loaded && deltaTime < 1.0) continue;
                data.lastUpdateTime = currentTime;

                // If time went backwards or there was a big spike, reset.
                if (deltaTime < 0) return;

                data.cabinCO2 = ctx.cabinCO2;

                /*
                if (v == FlightGlobals.ActiveVessel)
                    Debug.Log($"[KICKLS] Processing Active Vessel: {v.vesselName} | Crew: {liveCrew} | dT: {deltaTime}");
                */

                BreatheAir(ctx, data, deltaTime);
                RunScrubber(ctx, data, deltaTime);
                RunClimateControl(ctx, data, deltaTime);
                EatFood(v, data, deltaTime, ctx.liveCrew);
                DrinkWater(v, data, deltaTime, ctx.liveCrew);
                MonitorTemperature(ctx, data, deltaTime);

                CheckGraceAnnouncements(data, v);
                CheckConditions(ctx, data, deltaTime);

                // Redistribute CO2
                if (ctx.cabinCapacity > 0)
                {
                    SetCabinCO2(ctx, data.cabinCO2);
                }
            }
        }

        /// <summary>
        /// Checks if the vessel is valid for our purposes
        /// </summary>
        /// <param name="vessel"></param>
        /// <returns></returns>
        bool IsValidVessel(Vessel vessel)
        {
            if (vessel == null) return false;

            if ((vessel.vesselType == VesselType.Debris) ||
                (vessel.vesselType == VesselType.Flag) ||
                (vessel.vesselType == VesselType.SpaceObject) ||
                (vessel.vesselType == VesselType.Unknown))
            {
                return false;
            }

            // TODO: EVA life support?
            if (vessel.vesselType == VesselType.EVA) return false;

            if (vessel.state == Vessel.State.DEAD) return false;

            if (vessel.GetCrewCount() == 0) return false;

            if (!VesselHasLifeSupportModules(vessel)) return false;

            return true;
        }

        VesselLifeSupportContext BuildContext(Vessel vessel)
        {
            if (vessel == null) return null;

            if ((vessel.vesselType == VesselType.Debris) ||
                (vessel.vesselType == VesselType.Flag) ||
                (vessel.vesselType == VesselType.SpaceObject) ||
                (vessel.vesselType == VesselType.Unknown) ||
                vessel.vesselType == VesselType.EVA ||
                vessel.state == Vessel.State.DEAD ||
                vessel.GetCrewCount() == 0)
            {
                return null;
            }

            VesselLifeSupportContext ctx = new VesselLifeSupportContext
            {
                vessel = vessel,
                loaded = vessel.loaded,
                ambientSafe = IsAmbientAtmosphereSafe(vessel),
                underwater = IsVesselUnderwater(vessel)
            };

            if (vessel.loaded)
            {
                foreach (Part part in vessel.parts)
                {
                    if (part == null) continue;

                    KickLifeSupportModule lifeSupport = part.FindModuleImplementing<KickLifeSupportModule>();
                    if (lifeSupport != null && lifeSupport.lifeSupportEnabled)
                    {
                        ctx.lifeSupportModules.Add(lifeSupport);
                        ctx.liveCrew += part.protoModuleCrew.Count;

                        if (lifeSupport.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                        {
                            ctx.ambientDependentCrew += part.protoModuleCrew.Count;
                        }
                        else
                        {
                            ctx.pressurizedCrew += part.protoModuleCrew.Count;
                            ctx.cabinCapacity += part.CrewCapacity;
                            ctx.cabinCO2 += lifeSupport.cabinCO2;
                        }
                    }

                    if (gameSettings != null && gameSettings.useCabinTempSystem)
                    {
                        KickTemperatureControlModule temperature = part.FindModuleImplementing<KickTemperatureControlModule>();
                        if (temperature != null)
                        {
                            ctx.temperatureModules.Add(temperature);
                        }
                    }
                }
            }
            else
            {
                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                {
                    if (part == null) continue;
                    int capacity = 0;
                    if (part.partInfo != null && part.partInfo.partPrefab != null)
                    {
                        capacity = part.partInfo.partPrefab.CrewCapacity;
                    }

                    int crew = part.protoModuleCrew.Count;

                    foreach (ProtoPartModuleSnapshot module in part.modules)
                    {
                        if (module.moduleName == "KickLifeSupportModule" && IsLifeSupportModuleEnabled(module))
                        {
                            ProtoLifeSupportPart record = new ProtoLifeSupportPart
                            {
                                part = part,
                                module = module,
                                crew = crew,
                                capacity = capacity,
                                atmosphereControlMode = GetAtmosphereControlMode(module),
                                atmosphericControlEnabled = IsAtmosphericControlEnabled(module)
                            };

                            ctx.protoLifeSupportParts.Add(record);
                            ctx.liveCrew += crew;

                            if (record.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                            {
                                ctx.ambientDependentCrew += crew;
                            }
                            else
                            {
                                ctx.pressurizedCrew += crew;
                                ctx.cabinCapacity += capacity;
                                if (float.TryParse(module.moduleValues.GetValue("cabinCO2"), out float co2))
                                {
                                    ctx.cabinCO2 += co2;
                                }
                            }
                        }
                        else if (module.moduleName == "KickTemperatureControlModule")
                        {
                            double ecRate = 0.03;
                            string val = module.moduleValues.GetValue("systemECRate");
                            if (val != null) double.TryParse(val, out ecRate);

                            val = module.moduleValues.GetValue("climateControlEnabled");
                            bool enabled = val == null || (bool.TryParse(val, out bool bVal) && bVal);

                            ctx.protoTemperatureParts.Add(new ProtoTemperaturePart
                            {
                                part = part,
                                module = module,
                                crew = crew,
                                capacity = capacity,
                                enabled = enabled,
                                ecRate = ecRate
                            });
                        }
                    }
                }
            }

            if (ctx.loaded && ctx.lifeSupportModules.Count == 0) return null;
            if (!ctx.loaded && ctx.protoLifeSupportParts.Count == 0) return null;
            if (ctx.liveCrew == 0) return null;

            ctx.occupancyScale = ctx.cabinCapacity > 0 ? Math.Min((double)ctx.pressurizedCrew / ctx.cabinCapacity, 1.0) : 0.0;
            return ctx;
        }

        #region Save/Load

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            CleanupDatabase();
            foreach (KeyValuePair<Guid, LifeSupportStatus> entry in database)
            {
                ConfigNode vesselNode = node.AddNode("VESSEL_DATA");
                vesselNode.AddValue("id", entry.Key.ToString());
                entry.Value.Save(vesselNode);
            }

            Debug.Log("[KICKLS] Saved " + database.Count + " vessels.");
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);

            database.Clear();

            ConfigNode[] vesselNodes = node.GetNodes("VESSEL_DATA");
            foreach (ConfigNode n in vesselNodes)
            {
                string idString = n.GetValue("id");
                if (string.IsNullOrEmpty(idString)) continue;
                Guid id = new Guid(idString);

                LifeSupportStatus data = new LifeSupportStatus();
                data.Load(n);

                database.Add(id, data);
            }

            Debug.Log("[KICKLS] Loaded " + database.Count + " vessels.");
        }

        #endregion

        #region Database Functions
        /// <summary>
        /// Gets a vessel's life support status. If the vessel doesn't have one, make one.
        /// </summary>
        /// <param name="vesselId"></param>
        /// <returns></returns>
        public LifeSupportStatus GetData(Guid vesselId)
        {
            if (database.ContainsKey(vesselId))
            {
                return database[vesselId];
            }

            // If the ship is just launched, undocked, etc
            LifeSupportStatus newStatus = new LifeSupportStatus();
            database.Add(vesselId, newStatus);
            return newStatus;
        }

        void CleanupDatabase()
        {
            List<Guid> idsToRemove = new List<Guid>();

            foreach (Guid id in database.Keys)
            {
                Vessel v = FlightGlobals.FindVessel(id);

                // 1. If v is null, the ship doesn't exist anymore (Terminated/Recovered). DELETE.
                if (v == null)
                {
                    idsToRemove.Add(id);
                    continue;
                }

                // 2. If it's a Flag or Asteroid, we never want to save data for it. DELETE.
                // (Note: We DO keep Debris, in case you dock to it later to salvage supplies)
                if (v.vesselType == VesselType.Flag || v.vesselType == VesselType.SpaceObject || v.vesselType == VesselType.Unknown)
                {
                    idsToRemove.Add(id);
                    continue;
                }

                // 3. DO NOT check Crew Count here. Keep data for empty ships!
            }

            foreach (Guid id in idsToRemove)
            {
                database.Remove(id);
            }
        }

        #endregion

        #region Settings & Resources

        int GetSafeId(string name)
        {
            PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(name);
            return (def != null) ? def.id : -1;
        }

        void GetResourceIds()
        {
            o2Id = GetSafeId("Oxygen");
            co2Id = GetSafeId("CarbonDioxide");
            foodId = GetSafeId("Food");
            waterId = GetSafeId("Water");
            wasteId = GetSafeId("Waste");
            wasteWaterId = GetSafeId("WasteWater");
            lithiumHydroxideId = GetSafeId("LithiumHydroxide");
            electricChargeId = GetSafeId("ElectricCharge");
        }

        void GetSettings()
        {
            gameSettings = HighLogic.CurrentGame.Parameters.CustomParams<KickLifeSupportSettings>();

            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KICKLS_SETTINGS");
            if (nodes.Length == 0)
            {
                Debug.LogError("[KickLifeSupport] No KICKLS_SETTINGS found");

                // --- DEBUG ---
                ScreenMessages.PostScreenMessage("[KICKLS] ERROR: Settings not found!", 10f, ScreenMessageStyle.UPPER_CENTER);
                // -------------

                this.enabled = false;
                return;
            }

            ConfigNode settings = nodes[0];
            o2RequestRate = GetValue(settings, "OXYGEN_RATE");
            co2RequestRate = GetValue(settings, "CO2_RATE");
            scrubberRequestRate = GetValue(settings, "SCRUBBER_RATE");
            foodRequestRate = GetValue(settings, "FOOD_RATE");
            waterRequestRate = GetValue(settings, "WATER_RATE");
            wasteRequestRate = GetValue(settings, "WASTE_RATE");
            wasteWaterRequestRate = GetValue(settings, "WASTEWATER_RATE");
            lithiumHydroxideRequestRate = GetValue(settings, "LITHIUMHYDROXIDE_RATE");
            scrubberECRequestRate = GetValue(settings, "SCRUBBER_EC_RATE");
            regenerativeScrubberECRequestRate = GetValue(settings, "REGENERATIVE_SCRUBBER_EC_RATE");
            openLoopELSECRequestRate = GetValue(settings, "OPEN_LOOP_ELS_EC_RATE");
            openLoopELSOxygenMultiplier = GetValue(settings, "OPEN_LOOP_ELS_OXYGEN_MULTIPLIER");
            if (openLoopELSOxygenMultiplier <= 0) openLoopELSOxygenMultiplier = 5f;

            graceO2 = GetValue(settings, "GRACE_OXYGEN");
            graceFood = GetValue(settings, "GRACE_FOOD");
            graceWater = GetValue(settings, "GRACE_WATER");
            graceClimate = GetValue(settings, "GRACE_CLIMATE");
            graceTemp = GetValue(settings, "GRACE_TEMP");
            ambientPressureMinimum = GetValue(settings, "AMBIENT_PRESSURE_MINIMUM");
            if (ambientPressureMinimum <= 0) ambientPressureMinimum = 50f;
            AmbientPressureMinimumKPa = ambientPressureMinimum;
            openLoopELSPressureMinimum = GetValue(settings, "OPEN_LOOP_ELS_PRESSURE_MINIMUM");
            if (openLoopELSPressureMinimum <= 0) openLoopELSPressureMinimum = 5f;
            OpenLoopELSPressureMinimumKPa = openLoopELSPressureMinimum;

            kerbalHeat = GetValue(settings, "KERBAL_HEAT");
        }

        float GetValue(ConfigNode node, string key)
        {
            if (float.TryParse(node.GetValue(key), out float value))
            {
                return value;
            }
            Debug.LogError("[KickLifeSupport] Invalid value for " + key);
            return 0f;
        }

        float GetTotalCO2(Vessel v)
        {
            float totalCo2 = 0;

            if (v.loaded)
            {
                // --- LOADED VESSEL ---
                List<KickLifeSupportModule> modules = v.FindPartModulesImplementing<KickLifeSupportModule>();

                foreach (KickLifeSupportModule m in modules)
                {
                    if (!m.lifeSupportEnabled) continue;
                    totalCo2 += m.cabinCO2;
                }
            }
            else
            {
                // --- UNLOADED VESSEL ---

                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartModuleSnapshot m in p.modules)
                    {
                        if (m.moduleName == "KickLifeSupportModule")
                        {
                            if (!IsLifeSupportModuleEnabled(m)) continue;
                            if (float.TryParse(m.moduleValues.GetValue("cabinCO2"), out float val))
                            {
                                totalCo2 += val;
                            }
                        }
                    }
                }
            }

            return totalCo2;
        }

        void SetCabinCO2(VesselLifeSupportContext ctx, float totalCO2)
        {
            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule m in ctx.lifeSupportModules)
                {
                    if (m.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                    {
                        m.cabinCO2 = 0;
                        continue;
                    }

                    float share = (float)m.part.CrewCapacity / ctx.cabinCapacity;
                    m.cabinCO2 = totalCO2 * share;
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    if (p.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                    {
                        p.module.moduleValues.SetValue("cabinCO2", 0);
                        continue;
                    }

                    if (p.capacity == 0) continue;
                    float share = (float)p.capacity / ctx.cabinCapacity;
                    p.module.moduleValues.SetValue("cabinCO2", totalCO2 * share);
                }
            }
        }

        #endregion

        #region Simulation

        void BreatheAir(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            ProcessOpenLoopELS(ctx, status, deltaTime);
            int unsafeAmbientCrew = ctx.ambientSafe ? 0 : ctx.ambientDependentCrew;

            (double co2Produced, double o2Ratio) resparation = ProcessConsumption(ctx.vessel, deltaTime, o2Id, o2RequestRate, co2Id, co2RequestRate, ctx.pressurizedCrew, false);

            // Todo: Add a player setting for debug data
            //Debug.Log($"[KICKLS] BreatheAir Result -> Produced: {resparation.co2Produced} | Ratio: {resparation.o2Ratio}");

            status.cabinCO2 += (float)resparation.co2Produced;
            status.ambientDependentCrew = ctx.ambientDependentCrew;
            status.ambientAtmosphereUnsafe = unsafeAmbientCrew > 0;
            status.ambientAtmosphereUnderwater = ctx.underwater;
            double co2Level = CalculateCabinCO2(status, ctx.cabinCapacity);

            if (resparation.o2Ratio < 0.99 || co2Level >= co2Fatal)
                status.lowO2Time += deltaTime;
            else
                status.lowO2Time = 0;

            if (unsafeAmbientCrew > 0)
                status.ambientExposureTime += deltaTime;
            else
                status.ambientExposureTime = 0;
        }

        void ProcessOpenLoopELS(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            status.activeOpenLoopELSVentCapacity = 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule m in ctx.lifeSupportModules)
                {
                    if (m.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopELS) continue;

                    int partCapacity = Math.Max(m.part.CrewCapacity, 1);

                    if (!m.scrubberEnabled)
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    if (!IsOpenLoopELSEnvironmentUsable(ctx))
                    {
                        m.SetAtmosphericControlStatus(GetOpenLoopELSEnvironmentStatus(ctx));
                        continue;
                    }

                    double ecReq = openLoopELSECRequestRate * deltaTime * partCapacity * ctx.occupancyScale;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecResult.ecRatio < 0.99)
                    {
                        m.SetAtmosphericControlStatus("No EC");
                        continue;
                    }

                    status.activeOpenLoopELSVentCapacity += partCapacity;
                    m.SetAtmosphericControlStatus("ELS Active");
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    if (p.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopELS) continue;

                    if (!p.atmosphericControlEnabled)
                    {
                        continue;
                    }
                    if (!IsOpenLoopELSEnvironmentUsable(ctx))
                    {
                        continue;
                    }

                    int partCapacity = Math.Max(p.capacity, 1);

                    double ecReq = openLoopELSECRequestRate * deltaTime * partCapacity * ctx.occupancyScale;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecResult.ecRatio < 0.99)
                    {
                        continue;
                    }

                    status.activeOpenLoopELSVentCapacity += partCapacity;
                }
            }

        }

        /// <summary>
        /// Scrubs CO2 out of the air
        /// </summary>
        /// <param name="v"></param>
        /// <param name="status"></param>
        /// <param name="deltaTime"></param>
        /// <param name="totalCrewOnShip"></param>
        /// <remarks>This whole method needs a rewrite, but a good one.</remarks>
        void RunScrubber(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            double totalRegenerativeRemoved = 0;
            double totalLiOHRemoved = 0;
            double totalVentedRemoved = 0;
            double activeRegenerativeSystemCapacity = 0;
            double activeLiOHSystemCapacity = 0;

            int activeRegenerativeCount = 0;
            int activeLiOHCount = 0;
            List<ScrubberContribution> contributions = ctx.scrubberContributions;
            contributions.Clear();

            // Rates per seat
            double baseScrubRate = scrubberRequestRate * deltaTime;
            double baseEcRate = scrubberECRequestRate * deltaTime;
            double baseRegenerativeScrubberEcRate = regenerativeScrubberECRequestRate * deltaTime;
            double availableCO2 = Math.Max(status.cabinCO2, 0);
            double lithiumHydroxidePerCO2 = scrubberRequestRate > epsilon ? lithiumHydroxideRequestRate / scrubberRequestRate : 0;
            double openLoopExtraOxygenPerCO2 = scrubberRequestRate > epsilon ? o2RequestRate * Math.Max(openLoopELSOxygenMultiplier - 1.0, 0) / scrubberRequestRate : 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule m in ctx.lifeSupportModules)
                {
                    if (m.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                    {
                        m.SetAtmosphericControlStatus(GetAmbientAtmosphereStatus(ctx));
                        continue;
                    }

                    if (m.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlPressurizedCabin)
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    if (m.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopELS)
                    {
                        int elsCapacity = Math.Max(m.part.CrewCapacity, 1);
                        if (!m.scrubberEnabled)
                        {
                            m.SetAtmosphericControlStatus(ctx.ambientSafe ? "Safe Env" : "Inactive");
                            if (ctx.ambientSafe)
                            {
                                contributions.Add(new ScrubberContribution
                                {
                                    module = m,
                                    mode = KickLifeSupportModule.AtmosphereControlOpenLoopELS,
                                    capacity = elsCapacity,
                                    loaded = true,
                                    usesOpenLoopOxygenAssist = false
                                });
                            }
                        }
                        else if (m.rawScrubberStatus == "ELS Active")
                        {
                            contributions.Add(new ScrubberContribution
                            {
                                module = m,
                                mode = KickLifeSupportModule.AtmosphereControlOpenLoopELS,
                                capacity = elsCapacity,
                                loaded = true,
                                usesOpenLoopOxygenAssist = true
                            });
                        }
                        continue;
                    }

                    if (!m.scrubberEnabled)
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    int partCapacity = m.part.CrewCapacity;
                    if (partCapacity == 0) partCapacity = 1;

                    double ecReq = IsRegenerativeScrubber(m.atmosphereControlMode) ?
                        baseRegenerativeScrubberEcRate * partCapacity * ctx.occupancyScale :
                        baseEcRate * partCapacity * ctx.occupancyScale;

                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);

                    if (ecRes.ratio < 0.99)
                    {
                        m.SetAtmosphericControlStatus("No EC");
                        continue;
                    }

                    contributions.Add(new ScrubberContribution
                    {
                        module = m,
                        mode = m.atmosphereControlMode,
                        capacity = partCapacity,
                        loaded = true
                    });
                }
            }
            else
            {
                if (status.activeOpenLoopELSVentCapacity > 0)
                {
                    contributions.Add(new ScrubberContribution
                    {
                        mode = KickLifeSupportModule.AtmosphereControlOpenLoopELS,
                        capacity = (int)Math.Max(status.activeOpenLoopELSVentCapacity, 1),
                        loaded = false,
                        usesOpenLoopOxygenAssist = true
                    });
                }

                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    bool isScrubberOn = p.atmosphericControlEnabled;
                    int atmosphereControlMode = p.atmosphereControlMode;
                    int partCapacity = Math.Max(p.capacity, 1);

                    if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                    {
                        continue;
                    }

                    if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlPressurizedCabin)
                    {
                        continue;
                    }

                    if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopELS)
                    {
                        if (!isScrubberOn && ctx.ambientSafe)
                        {
                            contributions.Add(new ScrubberContribution
                            {
                                protoPart = p,
                                mode = KickLifeSupportModule.AtmosphereControlOpenLoopELS,
                                capacity = partCapacity,
                                loaded = false,
                                usesOpenLoopOxygenAssist = false
                            });
                        }
                        continue;
                    }

                    if (!isScrubberOn) continue;

                    double ecReq = IsRegenerativeScrubber(atmosphereControlMode) ?
                        baseRegenerativeScrubberEcRate * partCapacity * ctx.occupancyScale :
                        baseEcRate * partCapacity * ctx.occupancyScale;
                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecRes.ratio < 0.99) continue;

                    contributions.Add(new ScrubberContribution
                    {
                        protoPart = p,
                        mode = atmosphereControlMode,
                        capacity = partCapacity,
                        loaded = false
                    });
                }
            }

            int totalCapacity = 0;
            foreach (ScrubberContribution contribution in contributions)
            {
                totalCapacity += Math.Max(contribution.capacity, 0);
            }

            if (totalCapacity > 0 && availableCO2 > epsilon)
            {
                double totalScrubRequest = Math.Min(availableCO2, baseScrubRate * totalCapacity);

                foreach (ScrubberContribution contribution in contributions)
                {
                    double requestedScrubAmount = totalScrubRequest * contribution.capacity / totalCapacity;
                    double actualScrubAmount = requestedScrubAmount;

                    if (IsRegenerativeScrubber(contribution.mode))
                    {
                        activeRegenerativeSystemCapacity += contribution.capacity;
                        totalRegenerativeRemoved += actualScrubAmount;
                        ProduceResource(ctx.vessel, co2Id, actualScrubAmount);
                        if (contribution.module != null) contribution.module.SetAtmosphericControlStatus("Active");
                        activeRegenerativeCount++;
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
                        activeLiOHSystemCapacity += contribution.capacity;
                        double liohReq = requestedScrubAmount * lithiumHydroxidePerCO2;
                        double liohTaken = contribution.loaded
                            ? ConsumeLoadedLiOH(contribution.module, liohReq)
                            : ConsumeProtoLiOH(ctx.vessel, contribution.protoPart, liohReq);

                        actualScrubAmount = lithiumHydroxidePerCO2 > epsilon ? Math.Min(requestedScrubAmount, liohTaken / lithiumHydroxidePerCO2) : 0;
                        totalLiOHRemoved += actualScrubAmount;

                        if (liohTaken < liohReq - epsilon)
                        {
                            if (contribution.module != null) contribution.module.SetAtmosphericControlStatus("No LiOH");
                        }
                        else
                        {
                            if (contribution.module != null) contribution.module.SetAtmosphericControlStatus("Active");
                        }

                        if (actualScrubAmount > epsilon) activeLiOHCount++;
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlOpenLoopELS)
                    {
                        if (contribution.usesOpenLoopOxygenAssist)
                        {
                            double o2Req = requestedScrubAmount * openLoopExtraOxygenPerCO2;
                            (double o2Consumed, double o2Ratio) o2Result = ConsumeResource(ctx.vessel, o2Id, o2Req);

                            if (o2Result.o2Ratio < 0.99)
                            {
                                if (contribution.module != null) contribution.module.SetAtmosphericControlStatus("ELS Active");
                            }
                            else if (contribution.module != null)
                            {
                                contribution.module.SetAtmosphericControlStatus("ELS Active");
                            }
                        }

                        totalVentedRemoved += actualScrubAmount;
                    }
                }
            }
            else if (totalCapacity > 0)
            {
                foreach (ScrubberContribution contribution in contributions)
                {
                    if (contribution.module == null) continue;

                    if (IsRegenerativeScrubber(contribution.mode) ||
                        contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
                        if (IsRegenerativeScrubber(contribution.mode))
                            activeRegenerativeSystemCapacity += contribution.capacity;
                        else
                            activeLiOHSystemCapacity += contribution.capacity;

                        contribution.module.SetAtmosphericControlStatus("Active");
                    }
                    else if (contribution.usesOpenLoopOxygenAssist)
                    {
                        contribution.module.SetAtmosphericControlStatus("ELS Active");
                    }
                }
            }

            status.cabinCO2 -= (float)(totalRegenerativeRemoved + totalLiOHRemoved + totalVentedRemoved);
            status.lastRegenerativeScrubAmount = totalRegenerativeRemoved;
            status.lastLiOHScrubAmount = totalLiOHRemoved;
            status.activeRegenerativeScrubberSystemCapacity = activeRegenerativeSystemCapacity;
            status.activeLiOHSystemCapacity = activeLiOHSystemCapacity;
            status.activeRegenerativeScrubberCount = activeRegenerativeCount;
            status.activeLiOHScrubberCount = activeLiOHCount;
            if (status.cabinCO2 < 0) status.cabinCO2 = 0;
        }

        double ConsumeLoadedLiOH(KickLifeSupportModule module, double amount)
        {
            if (module == null || amount <= epsilon) return 0;

            double taken = module.part.RequestResource(lithiumHydroxideId, amount);
            if (taken > epsilon)
            {
                module.part.RequestResource(wasteId, -taken);
            }

            return taken;
        }

        double ConsumeProtoLiOH(Vessel vessel, ProtoLifeSupportPart record, double amount)
        {
            if (record == null || record.part == null || amount <= epsilon) return 0;

            double taken = 0;
            foreach (ProtoPartResourceSnapshot r in record.part.resources)
            {
                if (r.definition.id != lithiumHydroxideId) continue;

                if (r.amount < amount)
                {
                    TryReloadScrubberUnloaded(vessel, record.part);
                }

                taken = Math.Min(r.amount, amount);
                r.amount -= taken;
                break;
            }

            if (taken > epsilon)
            {
                AddProtoWaste(record.part, taken);
            }

            return taken;
        }

        void AddProtoWaste(ProtoPartSnapshot part, double amount)
        {
            if (part == null || amount <= epsilon) return;

            double remaining = amount;
            foreach (ProtoPartResourceSnapshot r in part.resources)
            {
                if (r.definition.id != wasteId) continue;

                double space = r.maxAmount - r.amount;
                double added = Math.Min(space, remaining);
                r.amount += added;
                remaining -= added;
                if (remaining <= epsilon) break;
            }
        }

        /// <summary>
        /// Kerbals eat food, poop out waste
        /// </summary>
        /// <param name="v"></param>
        /// <param name="status"></param>
        /// <param name="deltaTime"></param>
        /// <param name="crewCount"></param>
        void EatFood(Vessel v, LifeSupportStatus status, double deltaTime, int crewCount)
        {
            if (ProcessConsumption(v, deltaTime, foodId, foodRequestRate, wasteId, wasteRequestRate, crewCount, true).resourceARatio < 0.99)
                status.lowFoodTime += deltaTime;
            else
                status.lowFoodTime = 0;
        }

        /// <summary>
        /// Consumes water and makes wastewater
        /// </summary>
        /// <param name="deltaTime"></param>
        void DrinkWater(Vessel v, LifeSupportStatus status, double deltaTime, int crewCount)
        {
            if (ProcessConsumption(v, deltaTime, waterId, waterRequestRate, wasteWaterId, wasteWaterRequestRate, crewCount, true).resourceARatio < 0.99)
                status.lowWaterTime += deltaTime;
            else
                status.lowWaterTime = 0;
        }

        /// <summary>
        /// Calcualtes the cabin CO2 concentration as a percentage
        /// </summary>
        /// <param name="crewCount"></param>
        double CalculateCabinCO2(LifeSupportStatus status, int crewCount)
        {
            if (double.IsNaN(status.cabinCO2) || double.IsInfinity(status.cabinCO2))
            {
                status.cabinCO2 = 0;
            }

            if (status.cabinCO2 < 0) status.cabinCO2 = 0;
            if (crewCount > 0 && airPerSeat > 0)
                return status.cabinCO2 / (float)(crewCount * airPerSeat);
            else
                return 0;
        }

        /// <summary>
        /// Runs climate control system (cabin heaters, fans, gylcol loop)
        /// </summary>
        /// <param name="v"></param>
        /// <param name="status"></param>
        /// <param name="deltaTime"></param>
        void RunClimateControl(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            if (!gameSettings.useCabinTempSystem)
            {
                return;
            }

            bool climateFailureDetected = false;

            if (!ctx.loaded)
            {
                foreach (ProtoTemperaturePart p in ctx.protoTemperatureParts)
                {
                    if (p.capacity == 0) continue;

                    if (!p.enabled)
                    {
                        if (p.crew > 0)
                            climateFailureDetected = true;
                        continue;
                    }

                    double ecReq = p.ecRate * p.capacity;
                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecRes.ratio < 0.99)
                    {
                        if (p.crew > 0)
                            climateFailureDetected = true;
                    }
                }
            }

            if (climateFailureDetected)
            {
                if (status.lsStatus == "Nominal")
                {
                    status.lsStatus = "Temp Control Failure";
                }
            }
        }

        void MonitorTemperature(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            if (!gameSettings.useCabinTempSystem)
            {
                return;
            }

            if (!ctx.loaded)
            {
                status.tempRangeTime = 0;
                return;
            }

            bool tempIssueDetected = false;

            foreach (KickTemperatureControlModule module in ctx.temperatureModules)
            {
                if (module.part.protoModuleCrew.Count == 0) continue;

                if (module.cabinTemp < minSafeTemp || module.cabinTemp > maxSafeTemp)
                {
                    tempIssueDetected = true;
                    status.lastCabinTemp = module.cabinTemp;
                    break;
                }
            }

            if (tempIssueDetected)
            {
                status.tempRangeTime += deltaTime;
            }
            else
            {
                status.tempRangeTime = 0;
            }
        }

        /// <summary>
        /// Checks to see if the crew should die
        /// </summary>
        void CheckConditions(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            double co2Level = CalculateCabinCO2(status, ctx.cabinCapacity);

            // PRIORITY 0: Nominal
            status.lsStatus = "Nominal";

            if (co2Level >= co2Fatal && status.lowO2Time >= graceO2)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' suffocated (CO2). Time unable to breathe: {status.lowO2Time:F1}s (Limit: {graceO2:F1}s); Level: {co2Level:P2}");
                KillCrew(ctx.vessel, "Crew lost to CO2 poisoning", $"Carbon dioxide concentration aboard {ctx.vessel.vesselName} reached fatal levels.");
                status.lsStatus = $"CO2 High ({FormatRemainingTime(0)})";
                return;
            }

            if (status.ambientExposureTime >= graceO2)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Ambient-dependent crew on '{ctx.vessel.vesselName}' suffocated. Time without breathable ambient air: {status.ambientExposureTime:F1}s (Limit: {graceO2:F1}s)");
                KillAmbientDependentCrew(ctx, "Crew lost to unbreathable ambient air", $"Crew in unpressurized compartments aboard {ctx.vessel.vesselName} were exposed to an unbreathable environment.");
                status.lsStatus = $"No Ambient Air ({FormatRemainingTime(0)})";
                status.ambientExposureTime = 0;
                return;
            }

            if (status.lowO2Time >= graceO2)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' suffocated (No O2). Time without Air: {status.lowO2Time:F1}s (Limit: {graceO2:F1}s)");
                KillCrew(ctx.vessel, "Crew lost to oxygen deprivation", $"The crew aboard {ctx.vessel.vesselName} ran out of breathable atmosphere.");
                status.lsStatus = $"No O2 ({FormatRemainingTime(0)})";
                return;
            }

            if (gameSettings.useCabinTempSystem)
            {
                if (status.lowClimateTime >= graceClimate)
                {
                    Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' suffocated (Temperature Control Failure). Time without Circulation: {status.lowClimateTime:F1}s (Limit: {graceClimate:F1}s)");
                    KillCrew(ctx.vessel, "Crew lost to atmospheric control failure", $"Temperature control failed aboard {ctx.vessel.vesselName} long enough for the cabin environment to become fatal.");
                    status.lsStatus = $"No O2 ({FormatRemainingTime(0)})";
                    return;
                }

                if (status.tempRangeTime >= graceTemp)
                {
                    Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' froze/cooked. Time out of range: {status.tempRangeTime:F1}s (Limit: {graceTemp:F1}s); Current Temp: {status.lastCabinTemp:F0}C");
                    KillCrew(ctx.vessel, "Crew lost to cabin temperature", $"Cabin temperature aboard {ctx.vessel.vesselName} stayed outside survivable limits.");
                    status.lsStatus = $"Hot ({FormatRemainingTime(0)})";
                    return;
                }
            }

            // PRIORITY 2: Oxygen and temperature can be shown together.
            string dangerStatus = BuildTimedSituationStatus(status, co2Level);
            if (!string.IsNullOrEmpty(dangerStatus))
            {
                status.lsStatus = dangerStatus;
                return;
            }

            // PRIORITY 3: Water (Medium Death)
            if (status.lowWaterTime >= graceWater)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' died of dehydration. Time without Water: {status.lowWaterTime:F1}s (Limit: {graceWater:F1}s)");
                KillCrew(ctx.vessel, "Crew lost to dehydration", $"The crew aboard {ctx.vessel.vesselName} ran out of water.");
                status.lsStatus = "Crew dehydrated!";
                return;
            }
            else if (status.lowWaterTime > 0)
            {
                status.lsStatus = $"Thirsty! ({graceWater - status.lowWaterTime:F0}s)";
                return;
            }

            // PRIORITY 4: Food (Slow Death)
            if (status.lowFoodTime >= graceFood)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' starved to death. Time without Food: {status.lowFoodTime:F1}s (Limit: {graceFood:F1}s)");
                KillCrew(ctx.vessel, "Crew lost to starvation", $"The crew aboard {ctx.vessel.vesselName} ran out of food.");
                status.lsStatus = "Crew starved!";
                return;
            }
            else if (status.lowFoodTime > 0)
            {
                status.lsStatus = $"Starving! ({graceFood - status.lowFoodTime:F0}s)";
                return;
            }

            
        }
        #endregion

        #region Resources

        /// <summary>
        /// Turns resourceA into resourceB based on crew count.
        /// </summary>
        /// <param name="resourceA">Input resource ID</param>
        /// <param name="resourceARate">Rate of input</param>
        /// <param name="resourceB">Output resource ID</param>
        /// <param name="resourceBRate">Rate of output</param>
        /// <param name="crewCount">Number of live crew</param>
        /// <param name="store">Determines whether to store the output or throw it away</param>
        /// <returns>Returns a Tuple with the amount of resource B produced and the ratio of resource A requested vs returned</returns>
        (double resourceBProduced, double resourceARatio) ProcessConsumption(Vessel v, double deltaTime, int resourceA, float resourceARate, int resourceB, float resourceBRate, int crewCount, bool store = true)
        {
            double rARequestRate = resourceARate * deltaTime * crewCount;
            if (rARequestRate <= epsilon) return (0, 1.0);

            (double consumed, double ratio) resultA  = ConsumeResource(v, resourceA, rARequestRate);
            //Debug.Log($"[KICKLS] ConsumeResource returned: {resultA.consumed} (Ratio: {resultA.ratio})");

            double rBProduced = resultA.ratio * resourceBRate * deltaTime * crewCount;
            if (store)
            {
                double amountActuallyAdded = ProduceResource(v, resourceB, rBProduced);

            }
            return (rBProduced, resultA.ratio);
        }

        public double GetResourceTotal(Vessel v, int resourceID)
        {
            double total = 0;
            if (v.loaded)
            {
                foreach (Part p in v.parts)
                {
                    foreach (PartResource r in p.Resources)
                    {
                        if (r.info.id == resourceID) total += r.amount;
                    }
                }
            }
            else
            {
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    foreach (ProtoPartResourceSnapshot r in p.resources)
                    {
                        if (r.definition.id == resourceID) total += r.amount;
                    }
                }
            }
            return total;
        }

        (double amountConsumed, double ratio) ConsumeResource(Vessel v, int resourceID, double amountToConsume)
        {
            if (amountToConsume <= epsilon) return (0, 1.0);

            double actuallyTaken = 0;

            // 1. Check the Resource Definition
            // We need to know if this resource obeys flow logic.
            PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(resourceID);
            if (def == null) return (0, 0);

            // 2. Determine Strategy
            // If it flows (O2/EC), let KSP handle the plumbing (Valves/Crossfeed).
            // If it doesn't flow (LiOH/Waste), we must manually find it on the ship.
            bool useKspApi = (def.resourceFlowMode != ResourceFlowMode.NO_FLOW);

            if (v.loaded && useKspApi)
            {
                // --- STRATEGY A: KSP NATIVE FLOW (Loaded + Flowable) ---
                // This respects valves, fuel lines, and docking ports.
                if (v.rootPart != null)
                {
                    actuallyTaken = v.rootPart.RequestResource(resourceID, amountToConsume);
                }
            }
            else
            {
                // --- STRATEGY B: MANUAL ITERATION ---
                // Used for:
                // 1. Unloaded Vessels (Background processing)
                // 2. NO_FLOW Resources (LiOH, Waste, Food packets)
                //    (Simulates crew manually fetching items from any container)

                if (v.loaded)
                {
                    // Manual Iteration for LOADED vessels
                    foreach (Part p in v.parts)
                    {
                        if (p.Resources == null) continue;

                        foreach (PartResource r in p.Resources)
                        {
                            if (r.info.id == resourceID)
                            {
                                double available = r.amount;
                                double needed = amountToConsume - actuallyTaken;

                                if (available >= needed)
                                {
                                    r.amount -= needed;
                                    actuallyTaken += needed;
                                }
                                else
                                {
                                    r.amount = 0;
                                    actuallyTaken += available;
                                }
                            }
                            if (actuallyTaken >= amountToConsume - epsilon) break;
                        }
                        if (actuallyTaken >= amountToConsume - epsilon) break;
                    }
                }
                else
                {
                    // Manual Iteration for UNLOADED vessels (ProtoSnapshots)
                    foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                    {
                        foreach (ProtoPartResourceSnapshot r in p.resources)
                        {
                            if (r.definition.id == resourceID)
                            {
                                double available = r.amount;
                                double needed = amountToConsume - actuallyTaken;

                                if (available >= needed)
                                {
                                    r.amount -= needed;
                                    actuallyTaken += needed;
                                }
                                else
                                {
                                    r.amount = 0;
                                    actuallyTaken += available;
                                }
                            }
                            if (actuallyTaken >= amountToConsume - epsilon) break;
                        }
                        if (actuallyTaken >= amountToConsume - epsilon) break;
                    }
                }
            }

            double ratio = (amountToConsume > 0) ? actuallyTaken / amountToConsume : 0;
            return (actuallyTaken, ratio);
        }

        /// <summary>
        /// Universal resource producer. Returns the amount actually added.
        /// </summary>
        double ProduceResource(Vessel v, int resourceID, double amountToProduce)
        {
            if (amountToProduce <= epsilon) return 0;

            PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(resourceID);
            if (def == null) return 0;

            bool useKspApi = (def.resourceFlowMode != ResourceFlowMode.NO_FLOW);
            double actuallyAdded = 0;

            if (v.loaded && useKspApi)
            {
                // Use KSP plumbing for fluids (CO2, WasteWater).
                // Requesting a negative amount ADDS the resource.
                // The API returns the amount added as a negative number.
                double result = v.rootPart.RequestResource(resourceID, -amountToProduce);

                // Flip the sign back to positive for our return value
                actuallyAdded = -result;
            }
            else
            {
                // Manual fill for Unloaded OR NO_FLOW items (Solid Waste)
                double remainingToAdd = amountToProduce;

                if (v.loaded)
                {
                    foreach (Part p in v.parts)
                    {
                        foreach (PartResource r in p.Resources)
                        {
                            if (r.info.id == resourceID)
                            {
                                double space = r.maxAmount - r.amount;

                                if (space >= remainingToAdd)
                                {
                                    r.amount += remainingToAdd;
                                    remainingToAdd = 0;
                                }
                                else
                                {
                                    r.amount = r.maxAmount; // Fill it up
                                    remainingToAdd -= space;
                                }
                            }
                            if (remainingToAdd <= epsilon) break;
                        }
                        if (remainingToAdd <= epsilon) break;
                    }
                }
                else
                {
                    // Unloaded Logic (ProtoSnapshots)
                    foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                    {
                        foreach (ProtoPartResourceSnapshot r in p.resources)
                        {
                            if (r.definition.id == resourceID)
                            {
                                double space = r.maxAmount - r.amount;

                                if (space >= remainingToAdd)
                                {
                                    r.amount += remainingToAdd;
                                    remainingToAdd = 0;
                                }
                                else
                                {
                                    r.amount = r.maxAmount;
                                    remainingToAdd -= space;
                                }
                            }
                            if (remainingToAdd <= epsilon) break;
                        }
                        if (remainingToAdd <= epsilon) break;
                    }
                }

                // Calculate what we managed to push in
                actuallyAdded = amountToProduce - remainingToAdd;
            }

            return actuallyAdded;
        }
        #endregion

        #region Crew Helpers

        bool VesselHasLifeSupportModules(Vessel v)
        {
            if (v.loaded)
            {
                foreach (KickLifeSupportModule module in v.FindPartModulesImplementing<KickLifeSupportModule>())
                {
                    if (module.lifeSupportEnabled) return true;
                }
                return false;
            }

            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot m in p.modules)
                {
                    if (m.moduleName == "KickLifeSupportModule" && IsLifeSupportModuleEnabled(m)) return true;
                }
            }
            return false;
        }

        int GetLiveCrew(Vessel v)
        {
            int crew = 0;

            if (v.loaded)
            {
                foreach (KickLifeSupportModule m in v.FindPartModulesImplementing<KickLifeSupportModule>())
                {
                    if (!m.lifeSupportEnabled) continue;
                    crew += m.part.protoModuleCrew.Count;
                }
            }
            else
            {
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    if (!PartHasLifeSupportModule(p)) continue;
                    crew += p.protoModuleCrew.Count;
                }
            }

            return crew;
        }

        int GetCabinAtmosphereCapacity(Vessel v)
        {
            int capacity = 0;

            if (v.loaded)
            {
                foreach (KickLifeSupportModule m in v.FindPartModulesImplementing<KickLifeSupportModule>())
                {
                    if (!m.lifeSupportEnabled) continue;
                    capacity += m.part.CrewCapacity;
                }
            }
            else
            {
                foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
                {
                    if (p.partInfo != null && p.partInfo.partPrefab != null)
                    {
                        foreach (ProtoPartModuleSnapshot m in p.modules)
                        {
                            if (m.moduleName != "KickLifeSupportModule") continue;
                            if (!IsLifeSupportModuleEnabled(m)) continue;
                            capacity += p.partInfo.partPrefab.CrewCapacity;
                        }
                    }
                }
            }

            return capacity;
        }

        bool PartHasLifeSupportModule(ProtoPartSnapshot part)
        {
            foreach (ProtoPartModuleSnapshot m in part.modules)
            {
                if (m.moduleName == "KickLifeSupportModule" && IsLifeSupportModuleEnabled(m)) return true;
            }
            return false;
        }

        bool IsLifeSupportModuleEnabled(ProtoPartModuleSnapshot module)
        {
            string value = module.moduleValues.GetValue("lifeSupportEnabled");
            return value == null || !bool.TryParse(value, out bool enabled) || enabled;
        }

        int GetAtmosphereControlMode(ProtoPartModuleSnapshot module)
        {
            if (int.TryParse(module.moduleValues.GetValue("atmosphereControlMode"), out int mode))
            {
                return mode;
            }

            return KickLifeSupportModule.AtmosphereControlLiOH;
        }

        bool IsRegenerativeScrubber(int atmosphereControlMode)
        {
            return atmosphereControlMode == KickLifeSupportModule.AtmosphereControlRegenerativeScrubber ||
                   atmosphereControlMode == KickLifeSupportModule.AtmosphereControlSolidAmine;
        }

        bool IsOpenLoopELSEnvironmentUsable(Vessel v)
        {
            if (v == null || v.mainBody == null) return false;
            if (IsVesselUnderwater(v)) return false;
            return v.staticPressurekPa >= OpenLoopELSPressureMinimumKPa;
        }

        bool IsOpenLoopELSEnvironmentUsable(VesselLifeSupportContext ctx)
        {
            if (ctx == null || ctx.vessel == null || ctx.vessel.mainBody == null) return false;
            if (ctx.underwater) return false;
            return ctx.vessel.staticPressurekPa >= OpenLoopELSPressureMinimumKPa;
        }

        string GetOpenLoopELSEnvironmentStatus(Vessel v)
        {
            if (IsVesselUnderwater(v)) return "Underwater";
            return "Thin Atmo";
        }

        string GetOpenLoopELSEnvironmentStatus(VesselLifeSupportContext ctx)
        {
            if (ctx.underwater) return "Underwater";
            return "Thin Atmo";
        }

        bool IsAtmosphericControlEnabled(ProtoPartModuleSnapshot module)
        {
            string value = module.moduleValues.GetValue("scrubberEnabled");
            return value == null || !bool.TryParse(value, out bool enabled) || enabled;
        }

        double GetCabinOccupancyScale(Vessel v, LifeSupportStatus status, int cabinCapacity)
        {
            if (cabinCapacity <= 0) return 0;

            int crew = GetLiveCrew(v);
            if (crew <= 0) return 0;

            return Math.Min((double)crew / cabinCapacity, 1.0);
        }

        bool IsAmbientAtmosphereSafe(Vessel v)
        {
            if (v == null || v.mainBody == null) return false;
            if (!v.mainBody.atmosphereContainsOxygen) return false;
            if (IsVesselUnderwater(v)) return false;
            return v.staticPressurekPa >= AmbientPressureMinimumKPa;
        }

        bool IsVesselUnderwater(Vessel v)
        {
            return v != null && v.mainBody != null && v.mainBody.ocean && v.altitude < -0.5;
        }

        string GetAmbientAtmosphereStatus(Vessel v)
        {
            if (IsVesselUnderwater(v)) return "Underwater";
            if (v == null || v.mainBody == null || !v.mainBody.atmosphereContainsOxygen) return "No O2 Atmo";
            return IsAmbientAtmosphereSafe(v) ? "Safe Env" : "Thin Atmo";
        }

        string GetAmbientAtmosphereStatus(VesselLifeSupportContext ctx)
        {
            if (ctx.underwater) return "Underwater";
            if (ctx.vessel == null || ctx.vessel.mainBody == null || !ctx.vessel.mainBody.atmosphereContainsOxygen) return "No O2 Atmo";
            return ctx.ambientSafe ? "Safe Env" : "Thin Atmo";
        }

        string BuildTimedSituationStatus(LifeSupportStatus status, double co2Level)
        {
            List<SituationHazard> hazards = new List<SituationHazard>();

            if (co2Level >= co2Warning)
            {
                if (co2Level >= co2Fatal)
                {
                    hazards.Add(new SituationHazard("CO2 High", graceO2 - status.lowO2Time));
                }
                else
                {
                    hazards.Add(new SituationHazard("CO2 High", -1));
                }
            }

            double oxygenRemaining = -1;
            if (status.lowO2Time >= graceO2)
            {
                oxygenRemaining = 0;
            }
            else if (status.lowO2Time > 0)
            {
                oxygenRemaining = graceO2 - status.lowO2Time;
            }

            if (gameSettings.useCabinTempSystem)
            {
                if (status.lowClimateTime >= graceClimate)
                {
                    oxygenRemaining = 0;
                }
                else if (status.lowClimateTime > 0)
                {
                    double climateRemaining = graceClimate - status.lowClimateTime;
                    oxygenRemaining = oxygenRemaining < 0 ? climateRemaining : Math.Min(oxygenRemaining, climateRemaining);
                }
            }

            if (oxygenRemaining >= 0)
            {
                hazards.Add(new SituationHazard("No O2", oxygenRemaining));
            }

            if (status.ambientExposureTime >= graceO2)
            {
                hazards.Add(new SituationHazard("No Ambient Air", 0));
            }
            else if (status.ambientExposureTime > 0)
            {
                hazards.Add(new SituationHazard("No Ambient Air", graceO2 - status.ambientExposureTime));
            }

            if (gameSettings.useCabinTempSystem)
            {
                if (status.tempRangeTime >= graceTemp)
                {
                    hazards.Add(new SituationHazard("Hot", 0));
                }
                else if (status.tempRangeTime > 0)
                {
                    hazards.Add(new SituationHazard("Hot", graceTemp - status.tempRangeTime));
                }
            }

            if (hazards.Count == 0) return string.Empty;
            if (hazards.Count == 1) return FormatHazard(hazards[0]);

            double shortestTimer = double.MaxValue;
            foreach (SituationHazard hazard in hazards)
            {
                if (hazard.remainingSeconds >= 0 && hazard.remainingSeconds < shortestTimer)
                {
                    shortestTimer = hazard.remainingSeconds;
                }
            }

            return shortestTimer == double.MaxValue ? "SNAFU" : $"SNAFU ({FormatRemainingTime(shortestTimer)})";
        }

        string FormatHazard(SituationHazard hazard)
        {
            return hazard.remainingSeconds >= 0
                ? $"{hazard.label} ({FormatRemainingTime(hazard.remainingSeconds)})"
                : hazard.label;
        }

        string FormatRemainingTime(double seconds)
        {
            return KickUIFormat.Timer(seconds);
        }

        void CheckGraceAnnouncements(LifeSupportStatus status, Vessel v)
        {
            UpdateGraceAnnouncement(
                status.lowO2Time > 0,
                ref status.breathingGraceAnnounced,
                v,
                "KICK Life Support: breathable atmosphere failing",
                $"{v.vesselName}: crew have {FormatRemainingTime(graceO2 - status.lowO2Time)} before oxygen deprivation becomes fatal.");

            UpdateGraceAnnouncement(
                status.ambientExposureTime > 0,
                ref status.ambientGraceAnnounced,
                v,
                "KICK Life Support: ambient air unbreathable",
                $"{v.vesselName}: unpressurized crew have {FormatRemainingTime(graceO2 - status.ambientExposureTime)} before exposure becomes fatal.");

            UpdateGraceAnnouncement(
                status.lowWaterTime > 0,
                ref status.waterGraceAnnounced,
                v,
                "KICK Life Support: water unavailable",
                $"{v.vesselName}: crew have {FormatRemainingTime(graceWater - status.lowWaterTime)} before dehydration becomes fatal.");

            UpdateGraceAnnouncement(
                status.lowFoodTime > 0,
                ref status.foodGraceAnnounced,
                v,
                "KICK Life Support: food unavailable",
                $"{v.vesselName}: crew have {FormatRemainingTime(graceFood - status.lowFoodTime)} before starvation becomes fatal.");

            if (gameSettings.useCabinTempSystem)
            {
                UpdateGraceAnnouncement(
                    status.lowClimateTime > 0,
                    ref status.climateGraceAnnounced,
                    v,
                    "KICK Life Support: temperature control failing",
                    $"{v.vesselName}: crew have {FormatRemainingTime(graceClimate - status.lowClimateTime)} before cabin circulation failure becomes fatal.");

                UpdateGraceAnnouncement(
                    status.tempRangeTime > 0,
                    ref status.tempGraceAnnounced,
                    v,
                    "KICK Life Support: cabin temperature unsafe",
                    $"{v.vesselName}: crew have {FormatRemainingTime(graceTemp - status.tempRangeTime)} before cabin temperature becomes fatal.");
            }
            else
            {
                status.climateGraceAnnounced = false;
                status.tempGraceAnnounced = false;
            }
        }

        void UpdateGraceAnnouncement(bool graceActive, ref bool alreadyAnnounced, Vessel v, string title, string message)
        {
            if (!graceActive)
            {
                alreadyAnnounced = false;
                return;
            }

            if (alreadyAnnounced) return;
            alreadyAnnounced = true;
            if (v != FlightGlobals.ActiveVessel) return;
            ScreenMessages.PostScreenMessage($"{title}\n{message}", 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        void PostCrewDeathReport(string title, string message)
        {
            try
            {
                MessageSystem.Instance.AddMessage(new MessageSystem.Message(
                    title,
                    message,
                    MessageSystemButton.MessageButtonColor.RED,
                    MessageSystemButton.ButtonIcons.ALERT));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[KICKLS] Failed to post crew death report: {ex.Message}");
            }
        }

        struct SituationHazard
        {
            public readonly string label;
            public readonly double remainingSeconds;

            public SituationHazard(string label, double remainingSeconds)
            {
                this.label = label;
                this.remainingSeconds = remainingSeconds;
            }
        }

        void KillCrew(Vessel v, string reportTitle, string reportBody)
        {
            // 1. Get a copy of the crew list
            // (We need a copy because we are about to modify the vessel's crew list)
            List<ProtoCrewMember> crewList = new List<ProtoCrewMember>(v.GetVesselCrew());

            bool anyoneDied = false;
            List<string> lostCrew = new List<string>();

            foreach (ProtoCrewMember crew in crewList)
            {
                // 2. Mark them as Dead in the main roster
                crew.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                anyoneDied = true;
                lostCrew.Add(crew.name);

                // 3. Remove them from the vessel structure
                if (v.loaded)
                {
                    // LOADED: Remove from the physical seat
                    // Try to find the part via the Kerbal's seat reference first
                    if (crew.seat != null && crew.seat.part != null)
                    {
                        crew.seat.part.RemoveCrewmember(crew);
                    }
                    else
                    {
                        // Fallback: Check all parts
                        foreach (Part p in v.parts)
                        {
                            if (p.protoModuleCrew.Contains(crew))
                            {
                                p.RemoveCrewmember(crew);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // UNLOADED: Remove from the Save Data (ProtoSnapshot)
                    foreach (ProtoPartSnapshot pps in v.protoVessel.protoPartSnapshots)
                    {
                        if (pps.HasCrew(crew.name))
                        {
                            pps.RemoveCrew(crew.name);
                            break; 
                        }
                    }
                }
            }

            // 4. Update the game state if the player is looking at it
            if (anyoneDied && v.loaded)
            {
                GameEvents.onVesselChange.Fire(v);
            }

            if (anyoneDied)
            {
                string names = lostCrew.Count > 0 ? string.Join(", ", lostCrew.ToArray()) : "Unknown crew";
                PostCrewDeathReport(reportTitle, $"{reportBody}\n\nLost crew: {names}");
            }
        }

        void KillAmbientDependentCrew(VesselLifeSupportContext ctx, string reportTitle, string reportBody)
        {
            if (ctx == null || ctx.vessel == null) return;

            bool anyoneDied = false;
            List<string> lostCrew = new List<string>();

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlNone) continue;

                    Part part = module.part;
                    List<ProtoCrewMember> crewList = new List<ProtoCrewMember>(part.protoModuleCrew);
                    foreach (ProtoCrewMember crew in crewList)
                    {
                        crew.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                        part.RemoveCrewmember(crew);
                        anyoneDied = true;
                        lostCrew.Add(crew.name);
                    }
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart record in ctx.protoLifeSupportParts)
                {
                    if (record.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlNone) continue;

                    List<ProtoCrewMember> crewList = new List<ProtoCrewMember>(record.part.protoModuleCrew);
                    foreach (ProtoCrewMember crew in crewList)
                    {
                        crew.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                        record.part.RemoveCrew(crew.name);
                        anyoneDied = true;
                        lostCrew.Add(crew.name);
                    }
                }
            }

            if (anyoneDied && ctx.vessel.loaded)
            {
                GameEvents.onVesselChange.Fire(ctx.vessel);
            }

            if (anyoneDied)
            {
                string names = lostCrew.Count > 0 ? string.Join(", ", lostCrew.ToArray()) : "Unknown crew";
                PostCrewDeathReport(reportTitle, $"{reportBody}\n\nLost crew: {names}");
            }
        }

        #endregion

        #region Background Support

        bool TryReloadScrubberUnloaded(Vessel v, ProtoPartSnapshot podPart)
        {
            string cartridgePartName = "KickLSLiOHCartridge";
            double cartridgeVolume = 1.5;

            ProtoPartResourceSnapshot liohRes = null;
            ProtoPartResourceSnapshot wasteRes = null;

            // Get the resources
            foreach (var r in podPart.resources)
            {
                if (r.definition.id == lithiumHydroxideId) liohRes = r;
                if (r.definition.id == wasteId) wasteRes = r;
            }

            if (liohRes == null)
            {
                Debug.Log($"[KICKLS] TryReloadScrubberUnloaded() - NO LiOH TANK!");
                return false;  // There's no LiOH tank on this pod. Abort! Abort!
            }

            double liOHToAdd = liohRes.maxAmount - liohRes.amount;
            if (liOHToAdd < liohRes.maxAmount * 0.1) return false;

            double wasteToStore = cartridgeVolume - liOHToAdd;

            // Check that there's a waste tank
            if (wasteRes != null)
            {
                // Check if waste is full
                if ((wasteRes.maxAmount - wasteRes.amount) < wasteToStore)
                {
                    Debug.Log($"[KICKLS] TryReloadScrubberUnloaded() - Waste Full!");
                    return false;
                }
            }
            // If there's no waste tank, don't worry, we'll figure that out later

            // Now look for a cartridge
            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot m in p.modules)
                {
                    if (m.moduleName == "ModuleInventoryPart")
                    {
                        ConfigNode inventoryNode = m.moduleValues.GetNode("STOREDPARTS");
                        ConfigNode[] storedParts = inventoryNode.GetNodes("STOREDPART");
                        for (int i = 0; i < storedParts.Length; i++)
                        {
                            ConfigNode itemNode = storedParts[i];
                            if (itemNode.GetValue("partName") == cartridgePartName)
                            {
                                // Okay, we got one

                                int quantity = 1;
                                // Count them
                                if (itemNode.HasValue("quantity"))
                                    int.TryParse(itemNode.GetValue("quantity"), out quantity);

                                if (quantity > 1)
                                {
                                    // Remove one and save
                                    quantity--;
                                    itemNode.SetValue("quantity", quantity.ToString());
                                }
                                else
                                {
                                    // Remove the last one and save
                                    m.moduleValues.RemoveNode(itemNode);
                                }

                                // Refill the LiOH tank
                                liohRes.amount = liohRes.maxAmount;

                                // Add waste
                                if (wasteRes != null)
                                {
                                    wasteRes.amount += wasteToStore;
                                    if (wasteRes.amount > wasteRes.maxAmount) wasteRes.amount = wasteRes.maxAmount;
                                    // Yes, I know this means that the cartridge can get thrown away for free
                                    Debug.Log($"[KICKLS] TryReloadScrubberUnloaded() - Cartridge Replaced");
                                }
                                else
                                {
                                    // There's no tank. Let's try to throw it away *somewhere*
                                    ProduceResource(v, wasteId, wasteToStore);
                                    Debug.Log($"[KICKLS] TryReloadScrubberUnloaded() - Cartridge Replaced (No Waste)");
                                }
                                Debug.Log($"[KICKLS] Scrubber Auto-Reloaded (Background) on {v.vesselName}.");
                                return true;
                            }
                        }
                    }
                }
            }
            Debug.Log($"[KICKLS] TryReloadScrubberUnloaded() - NO CARTRIDGE!");
            return false;
        }

        #endregion

    }
}

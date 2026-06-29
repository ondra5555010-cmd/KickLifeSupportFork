using JetBrains.Annotations;
using KSP.UI.Screens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using UnityEngine;

namespace KickLifeSupport
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class KickLifeSupportScenario : ScenarioModule
    {
        const double epsilon = 0.00000001;
        public static double AmbientPressureMinimumKPa { get; private set; } = 50.0;
        public static double OpenLoopPressureMinimumKPa { get; private set; } = 3.15;
        public static double UnpressurizedPressureFailureKPa { get; private set; } = 6.3;

        public static KickLifeSupportScenario Instance { get; private set; }
        KickLifeSupportSettings gameSettings;

        public double GraceOxygen => graceO2;
        public double GraceWater => graceWater;
        public double GraceFood => graceFood;
        public double GraceClimate => graceClimate;
        public double GraceTemp => graceTemp;
        public double GraceUnpressurized => graceUnpressurized;
        public double CO2WarningLevel => co2Warning;
        public double CO2FatalLevel => co2Fatal;
        public double LithiumHydroxidePerCO2 =>
            scrubberRequestRate > 0 ? lithiumHydroxideRequestRate / scrubberRequestRate : 0;
        public double GetOpenLoopExtraOxygenPerCO2(double oxygenMultiplier)
        {
            return scrubberRequestRate > 0
                ? o2RequestRate * Math.Max(oxygenMultiplier - 1.0, 0) /
                    scrubberRequestRate
                : 0;
        }

        public Dictionary<Guid, LifeSupportStatus> database = new Dictionary<Guid, LifeSupportStatus>();
        readonly Dictionary<Guid, CachedVesselContext> vesselContextCache =
            new Dictionary<Guid, CachedVesselContext>();

        class VesselLifeSupportContext
        {
            public Vessel vessel;
            public bool loaded;
            public int liveCrew;
            public int cabinCrew;
            public int pressurizedCrew;
            public int ambientDependentCrew;
            public int cabinCapacity;
            public double cabinAirVolume;
            public float cabinCO2;
            public bool ambientSafe;
            public bool underwater;
            public double occupancyScale;
            public bool unattendedCO2Purge;
            public readonly List<KickLifeSupportModule> lifeSupportModules = new List<KickLifeSupportModule>();
            public readonly List<ProtoLifeSupportPart> protoLifeSupportParts = new List<ProtoLifeSupportPart>();
            public readonly List<ScrubberContribution> scrubberContributions = new List<ScrubberContribution>();
        }

        class CachedVesselContext
        {
            public Vessel vessel;
            public bool loaded;
            public int lastRefreshFrame = -1;
            public double lastRefreshTime = double.NaN;
            public readonly VesselLifeSupportContext context = new VesselLifeSupportContext();
            public readonly List<KickLifeSupportModule> discoveredModules = new List<KickLifeSupportModule>();
            public readonly List<ProtoLifeSupportPart> discoveredProtoParts = new List<ProtoLifeSupportPart>();
        }

        class ProtoLifeSupportPart
        {
            public ProtoPartSnapshot part;
            public ProtoPartModuleSnapshot module;
            public int crew;
            public int capacity;
            public int atmosphereControlMode;
            public bool atmosphericControlEnabled;
            public double atmosphericControlECRate;
            public double airVolumePerSeat;
            public double openLoopOxygenMultiplier;
            public bool openLoopActive;
            public double pressureExposureTime;
            public bool thermalControlEnabled;
            public double thermalControlECRate;
        }

        struct ScrubberContribution
        {
            public KickLifeSupportModule module;
            public ProtoLifeSupportPart protoPart;
            public int mode;
            public int capacity;
            public bool loaded;
            public bool usesOpenLoopOxygenAssist;
            public double openLoopOxygenMultiplier;
        }

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
        // Grace Periods
        float graceO2;
        float graceWater;
        float graceFood;
        float graceClimate;
        float graceTemp;
        float graceUnpressurized;
        float ambientPressureMinimum;
        float openLoopPressureMinimum;
        float unpressurizedPressureFailure;

        // Heat Generation
        public float kerbalHeat;

        float co2Warning;
        float co2Fatal;
        float minSafeTemp;
        float maxSafeTemp;

        #endregion

        public override void OnAwake()
        {
            Instance = this;

            GameEvents.onVesselWasModified.Add(InvalidateVesselContext);
            GameEvents.onVesselGoOnRails.Add(InvalidateVesselContext);
            GameEvents.onVesselDestroy.Add(RemoveVesselContext);

            GetSettings();
            GetResourceIds();

            if (o2Id == -1) Debug.LogError("[KICKLS] CRITICAL: Oxygen Resource ID not found!");
            if (o2RequestRate <= 0) Debug.LogError("[KICKLS] CRITICAL: Oxygen Rate is 0!");
        }

        public void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(InvalidateVesselContext);
            GameEvents.onVesselGoOnRails.Remove(InvalidateVesselContext);
            GameEvents.onVesselDestroy.Remove(RemoveVesselContext);
            vesselContextCache.Clear();
            if (Instance == this) Instance = null;
        }

        void InvalidateVesselContext(Vessel vessel)
        {
            if (vessel != null) vesselContextCache.Remove(vessel.id);
        }

        void RemoveVesselContext(Vessel vessel)
        {
            if (vessel == null) return;
            vesselContextCache.Remove(vessel.id);
            database.Remove(vessel.id);
        }

        public void FixedUpdate()
        {
            double currentTime = Planetarium.GetUniversalTime();

            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (!IsPotentialLifeSupportVessel(v)) continue;

                VesselLifeSupportContext ctx = BuildContext(v, currentTime);
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

                // If time went backwards or there was a big spike, reset.
                if (deltaTime < 0) return;

                data.lastUpdateTime = currentTime;
                data.cabinCO2 = ctx.cabinCO2;

                if (ctx.liveCrew == 0)
                {
                    ResetCrewHazardState(data);
                    ResetAtmosphericControlRuntime(ctx);
                    if (ctx.unattendedCO2Purge)
                    {
                        ProcessOpenLoopVentilation(ctx, data, deltaTime);
                        RunScrubber(
                            ctx,
                            data,
                            deltaTime,
                            co2Warning * ctx.cabinAirVolume);
                    }

                    if (ctx.cabinCapacity > 0)
                    {
                        SetCabinCO2(ctx, data.cabinCO2);
                    }
                    continue;
                }

                ResetAtmosphericControlRuntime(ctx);
                UpdatePressureExposure(ctx, data, deltaTime);
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

        bool IsPotentialLifeSupportVessel(Vessel vessel)
        {
            if (vessel == null) return false;
            if (vessel.vesselType == VesselType.Debris ||
                vessel.vesselType == VesselType.Flag ||
                vessel.vesselType == VesselType.SpaceObject ||
                vessel.vesselType == VesselType.Unknown ||
                vessel.vesselType == VesselType.EVA ||
                vessel.state == Vessel.State.DEAD)
            {
                return false;
            }

            return true;
        }

        VesselLifeSupportContext BuildContext(Vessel vessel, double refreshTime)
        {
            if (!IsPotentialLifeSupportVessel(vessel)) return null;

            CachedVesselContext cached = GetOrCreateCachedContext(vessel);
            if (cached == null) return null;
            if (cached.lastRefreshFrame == Time.frameCount &&
                cached.lastRefreshTime == refreshTime)
            {
                return cached.context;
            }

            cached.lastRefreshFrame = Time.frameCount;
            cached.lastRefreshTime = refreshTime;
            VesselLifeSupportContext ctx = cached.context;
            ctx.vessel = vessel;
            ctx.loaded = vessel.loaded;
            ctx.liveCrew = 0;
            ctx.cabinCrew = 0;
            ctx.pressurizedCrew = 0;
            ctx.ambientDependentCrew = 0;
            ctx.cabinCapacity = 0;
            ctx.cabinAirVolume = 0;
            ctx.cabinCO2 = 0;
            ctx.ambientSafe = IsAmbientAtmosphereSafe(vessel);
            ctx.underwater = IsVesselUnderwater(vessel);
            ctx.occupancyScale = 0;
            ctx.unattendedCO2Purge = false;
            ctx.lifeSupportModules.Clear();
            ctx.protoLifeSupportParts.Clear();
            ctx.scrubberContributions.Clear();

            if (cached.loaded)
            {
                foreach (KickLifeSupportModule lifeSupport in cached.discoveredModules)
                {
                    if (lifeSupport == null || lifeSupport.part == null ||
                        !lifeSupport.lifeSupportEnabled) continue;

                    ctx.lifeSupportModules.Add(lifeSupport);
                    int crew = lifeSupport.part.protoModuleCrew.Count;
                    ctx.liveCrew += crew;
                    AccumulateCabinContext(
                        ctx,
                        lifeSupport.atmosphereControlMode,
                        crew,
                        lifeSupport.part.CrewCapacity,
                        lifeSupport.airVolumePerSeat,
                        lifeSupport.cabinCO2);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart record in cached.discoveredProtoParts)
                {
                    if (record.part == null || record.module == null ||
                        !IsLifeSupportModuleEnabled(record.module)) continue;

                    RefreshProtoLifeSupportPart(record);
                    ctx.protoLifeSupportParts.Add(record);
                    ctx.liveCrew += record.crew;

                    float cabinCO2 = 0;
                    float.TryParse(
                        record.module.moduleValues.GetValue("cabinCO2"),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out cabinCO2);
                    AccumulateCabinContext(
                        ctx,
                        record.atmosphereControlMode,
                        record.crew,
                        record.capacity,
                        record.airVolumePerSeat,
                        cabinCO2);
                }
            }

            if (ctx.loaded && ctx.lifeSupportModules.Count == 0) return null;
            if (!ctx.loaded && ctx.protoLifeSupportParts.Count == 0) return null;

            double usableScrubberCapacity = GetUsableScrubberCapacity(ctx);
            if (ctx.liveCrew == 0)
            {
                double co2Level = ctx.cabinAirVolume > 0
                    ? ctx.cabinCO2 / ctx.cabinAirVolume
                    : 0;
                ctx.unattendedCO2Purge =
                    usableScrubberCapacity > 0 &&
                    co2Level > co2Warning;
                ctx.occupancyScale = ctx.unattendedCO2Purge ? 1.0 : 0.0;
                return ctx;
            }

            ctx.occupancyScale = usableScrubberCapacity > 0
                ? Math.Min(ctx.cabinCrew / usableScrubberCapacity, 1.0)
                : 0.0;
            return ctx;
        }

        void ResetCrewHazardState(LifeSupportStatus status)
        {
            status.lowO2Time = 0;
            status.ambientExposureTime = 0;
            status.ambientExposureRemaining = -1;
            status.lowWaterTime = 0;
            status.lowFoodTime = 0;
            status.lowClimateTime = 0;
            status.tempRangeTime = 0;
            status.breathingGraceAnnounced = false;
            status.ambientGraceAnnounced = false;
            status.waterGraceAnnounced = false;
            status.foodGraceAnnounced = false;
            status.climateGraceAnnounced = false;
            status.tempGraceAnnounced = false;
        }

        internal bool TryGetVesselCabinMetrics(
            Vessel vessel,
            out double cabinAirVolume,
            out float occupancyScale)
        {
            cabinAirVolume = 0;
            occupancyScale = 0;
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return false;

            VesselLifeSupportContext ctx =
                BuildContext(vessel, Planetarium.GetUniversalTime());
            if (ctx == null) return false;

            cabinAirVolume = ctx.cabinAirVolume;
            occupancyScale = (float)ctx.occupancyScale;
            return true;
        }

        CachedVesselContext GetOrCreateCachedContext(Vessel vessel)
        {
            if (vesselContextCache.TryGetValue(vessel.id, out CachedVesselContext cached) &&
                cached.vessel == vessel &&
                cached.loaded == vessel.loaded)
            {
                return cached;
            }

            cached = new CachedVesselContext
            {
                vessel = vessel,
                loaded = vessel.loaded
            };
            cached.context.vessel = vessel;
            cached.context.loaded = vessel.loaded;

            if (vessel.loaded)
            {
                foreach (Part part in vessel.parts)
                {
                    if (part == null) continue;
                    KickLifeSupportModule module =
                        part.FindModuleImplementing<KickLifeSupportModule>();
                    if (module != null) cached.discoveredModules.Add(module);
                }
            }
            else if (vessel.protoVessel != null)
            {
                foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
                {
                    if (part == null) continue;
                    int capacity =
                        part.partInfo != null && part.partInfo.partPrefab != null
                            ? part.partInfo.partPrefab.CrewCapacity
                            : 0;

                    foreach (ProtoPartModuleSnapshot module in part.modules)
                    {
                        if (module.moduleName != "KickLifeSupportModule") continue;
                        cached.discoveredProtoParts.Add(new ProtoLifeSupportPart
                        {
                            part = part,
                            module = module,
                            capacity = capacity
                        });
                    }
                }
            }

            vesselContextCache[vessel.id] = cached;
            return cached;
        }

        void RefreshProtoLifeSupportPart(ProtoLifeSupportPart record)
        {
            record.crew = record.part.protoModuleCrew.Count;
            record.atmosphereControlMode = GetAtmosphereControlMode(record.module);
            record.atmosphericControlEnabled = IsAtmosphericControlEnabled(record.module);
            KickLifeSupportModule prefabModule = GetPrefabLifeSupportModule(record.part);
            record.atmosphericControlECRate =
                GetAtmosphericControlECRate(
                    record.module,
                    record.atmosphereControlMode,
                    prefabModule);
            record.airVolumePerSeat =
                GetModuleDouble(
                    record.module,
                    "airVolumePerSeat",
                    prefabModule != null ? prefabModule.airVolumePerSeat : 2000);
            record.openLoopOxygenMultiplier =
                GetModuleDouble(
                    record.module,
                    "openLoopOxygenMultiplier",
                    prefabModule != null ? prefabModule.openLoopOxygenMultiplier : 10);
            record.openLoopActive = false;
            record.pressureExposureTime =
                GetModuleDouble(record.module, "pressureExposureTime");
            record.thermalControlEnabled =
                GetModuleBool(record.module, "climateControlEnabled", true);
            record.thermalControlECRate =
                GetModuleDouble(record.module, "systemECRate", 0.003);
        }

        void AccumulateCabinContext(
            VesselLifeSupportContext ctx,
            int atmosphereControlMode,
            int crew,
            int capacity,
            double airVolumePerSeat,
            float cabinCO2)
        {
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
            {
                if (!ctx.ambientSafe) ctx.ambientDependentCrew += crew;
                return;
            }

            if (KickLifeSupportModule.UsesOpenLoopVentilation(atmosphereControlMode) &&
                !IsOpenLoopPressureUsable(ctx))
            {
                ctx.ambientDependentCrew += crew;
                return;
            }

            ctx.cabinCapacity += capacity;
            ctx.cabinAirVolume += capacity * Math.Max(airVolumePerSeat, 0);
            ctx.cabinCO2 += cabinCO2;
            ctx.cabinCrew += crew;
            if (!KickLifeSupportModule.UsesOpenLoopVentilation(atmosphereControlMode) ||
                !ctx.ambientSafe)
            {
                ctx.pressurizedCrew += crew;
            }
        }

        double GetUsableScrubberCapacity(VesselLifeSupportContext ctx)
        {
            double capacity = 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (!module.scrubberEnabled || !UsesPoweredAtmosphericControl(module.atmosphereControlMode))
                        continue;
                    if (module.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation &&
                        !IsOpenLoopEnvironmentUsable(ctx))
                        continue;

                    capacity += Math.Max(module.part.CrewCapacity, 1);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (!part.atmosphericControlEnabled || !UsesPoweredAtmosphericControl(part.atmosphereControlMode))
                        continue;
                    if (part.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation &&
                        !IsOpenLoopEnvironmentUsable(ctx))
                        continue;

                    capacity += Math.Max(part.capacity, 1);
                }
            }

            return capacity;
        }

        bool UsesPoweredAtmosphericControl(int atmosphereControlMode)
        {
            return atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation ||
                   atmosphereControlMode == KickLifeSupportModule.AtmosphereControlLiOH ||
                   IsRegenerativeScrubber(atmosphereControlMode);
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

                ScreenMessages.PostScreenMessage("[KICKLS] ERROR: Settings not found!", 10f, ScreenMessageStyle.UPPER_CENTER);

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
            graceO2 = GetValue(settings, "GRACE_OXYGEN");
            graceFood = GetValue(settings, "GRACE_FOOD");
            graceWater = GetValue(settings, "GRACE_WATER");
            graceClimate = GetValue(settings, "GRACE_CLIMATE");
            graceTemp = GetValue(settings, "GRACE_TEMP");
            graceUnpressurized = GetValue(settings, "GRACE_UNPRESSURIZED");
            if (graceUnpressurized <= 0) graceUnpressurized = 15f;
            ambientPressureMinimum = GetValue(settings, "AMBIENT_PRESSURE_MINIMUM");
            if (ambientPressureMinimum <= 0) ambientPressureMinimum = 50f;
            AmbientPressureMinimumKPa = ambientPressureMinimum;
            openLoopPressureMinimum = GetValue(settings, "OPEN_LOOP_PRESSURE_MINIMUM");
            if (openLoopPressureMinimum <= 0) openLoopPressureMinimum = 3.15f;
            OpenLoopPressureMinimumKPa = openLoopPressureMinimum;
            unpressurizedPressureFailure = GetValue(settings, "UNPRESSURIZED_PRESSURE_FAILURE");
            if (unpressurizedPressureFailure <= 0) unpressurizedPressureFailure = 6.3f;
            UnpressurizedPressureFailureKPa = unpressurizedPressureFailure;

            kerbalHeat = GetValue(settings, "KERBAL_HEAT");
            co2Warning = KickLifeSupportConfig.GetFloat("CO2_WARNING_LEVEL", 0.03f);
            co2Fatal = KickLifeSupportConfig.GetFloat("CO2_FATAL_LEVEL", 0.10f);
            minSafeTemp = KickLifeSupportConfig.GetFloat("MIN_SAFE_CABIN_TEMP", 5f);
            maxSafeTemp = KickLifeSupportConfig.GetFloat("MAX_SAFE_CABIN_TEMP", 45f);
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
                    if (KickLifeSupportModule.UsesOpenLoopVentilation(m.atmosphereControlMode) &&
                        !IsOpenLoopPressureUsable(ctx))
                    {
                        continue;
                    }

                    double moduleAirVolume =
                        m.part.CrewCapacity * Math.Max(m.airVolumePerSeat, 0);
                    float share = ctx.cabinAirVolume > 0
                        ? (float)(moduleAirVolume / ctx.cabinAirVolume)
                        : 0;
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
                    if (KickLifeSupportModule.UsesOpenLoopVentilation(p.atmosphereControlMode) &&
                        !IsOpenLoopPressureUsable(ctx))
                    {
                        continue;
                    }

                    double moduleAirVolume =
                        p.capacity * Math.Max(p.airVolumePerSeat, 0);
                    if (moduleAirVolume <= 0) continue;
                    float share = ctx.cabinAirVolume > 0
                        ? (float)(moduleAirVolume / ctx.cabinAirVolume)
                        : 0;
                    p.module.moduleValues.SetValue("cabinCO2", totalCO2 * share);
                }
            }
        }

        #endregion

        #region Simulation

        void ResetAtmosphericControlRuntime(VesselLifeSupportContext ctx)
        {
            if (!ctx.loaded) return;

            foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
            {
                module.currentAtmosphericControlECRate = 0;
            }
        }

        void UpdatePressureExposure(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            double longestCrewExposure = 0;
            double shortestRemaining = double.MaxValue;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    bool exposed = IsPressureExposed(module.atmosphereControlMode, ctx);
                    module.pressureExposureTime = exposed
                        ? (float)Math.Max(module.pressureExposureTime + deltaTime, 0)
                        : 0;

                    if (exposed &&
                        KickLifeSupportModule.UsesOpenLoopVentilation(module.atmosphereControlMode) &&
                        module.pressureExposureTime >= graceUnpressurized)
                    {
                        module.cabinCO2 = 0;
                    }

                    if (exposed && module.part.protoModuleCrew.Count > 0)
                    {
                        longestCrewExposure = Math.Max(longestCrewExposure, module.pressureExposureTime);
                        shortestRemaining = Math.Min(
                            shortestRemaining,
                            GetPressureExposureGrace(module.atmosphereControlMode, ctx.vessel) -
                            module.pressureExposureTime);
                    }
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    bool exposed = IsPressureExposed(part.atmosphereControlMode, ctx);
                    part.pressureExposureTime = exposed
                        ? Math.Max(part.pressureExposureTime + deltaTime, 0)
                        : 0;
                    part.module.moduleValues.SetValue(
                        "pressureExposureTime",
                        part.pressureExposureTime.ToString("R", CultureInfo.InvariantCulture));

                    if (exposed &&
                        KickLifeSupportModule.UsesOpenLoopVentilation(part.atmosphereControlMode) &&
                        part.pressureExposureTime >= graceUnpressurized)
                    {
                        part.module.moduleValues.SetValue("cabinCO2", "0");
                    }

                    if (exposed && part.crew > 0)
                    {
                        longestCrewExposure = Math.Max(longestCrewExposure, part.pressureExposureTime);
                        shortestRemaining = Math.Min(
                            shortestRemaining,
                            GetPressureExposureGrace(part.atmosphereControlMode, ctx.vessel) -
                            part.pressureExposureTime);
                    }
                }
            }

            status.ambientExposureTime = longestCrewExposure;
            status.ambientExposureRemaining =
                shortestRemaining == double.MaxValue ? -1 : Math.Max(shortestRemaining, 0);
        }

        void BreatheAir(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            ProcessOpenLoopVentilation(ctx, status, deltaTime);
            int unsafeAmbientCrew = ctx.ambientSafe ? 0 : ctx.ambientDependentCrew;

            (double co2Produced, double o2Ratio) respiration = ProcessConsumption(
                ctx.vessel,
                deltaTime,
                o2Id,
                o2RequestRate,
                co2Id,
                co2RequestRate,
                ctx.pressurizedCrew,
                false);

            int ambientCabinCrew = Math.Max(ctx.cabinCrew - ctx.pressurizedCrew, 0);
            double ambientCabinCO2 =
                co2RequestRate * deltaTime * ambientCabinCrew;

            status.cabinCO2 +=
                (float)(respiration.co2Produced + ambientCabinCO2);
            double co2Level = CalculateCabinCO2(status, ctx.cabinAirVolume);

            if (respiration.o2Ratio < 0.99 || co2Level >= co2Fatal)
                status.lowO2Time += deltaTime;
            else
                status.lowO2Time = 0;

        }

        void ProcessOpenLoopVentilation(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime)
        {
            status.activeOpenLoopVentCapacity = 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule m in ctx.lifeSupportModules)
                {
                    if (m.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopVentilation) continue;

                    m.openLoopVentingActive = false;
                    int partCapacity = Math.Max(m.part.CrewCapacity, 1);

                    if (!m.scrubberEnabled)
                    {
                        m.SetAtmosphericControlStatus(
                            ctx.ambientSafe ? "Ambient Atmosphere" : "Inactive");
                        continue;
                    }

                    if (!IsOpenLoopEnvironmentUsable(ctx))
                    {
                        m.SetAtmosphericControlStatus(GetOpenLoopEnvironmentStatus(ctx));
                        continue;
                    }

                    double ecReq = Math.Max(m.atmosphericControlECRate, 0) * deltaTime * partCapacity * ctx.occupancyScale;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    m.currentAtmosphericControlECRate = deltaTime > epsilon
                        ? ecResult.ecConsumed / deltaTime
                        : 0;
                    if (ecResult.ecRatio < 0.99)
                    {
                        m.SetAtmosphericControlStatus("No EC");
                        continue;
                    }

                    status.activeOpenLoopVentCapacity += partCapacity;
                    m.openLoopVentingActive = true;
                    m.SetAtmosphericControlStatus(
                        ctx.ambientSafe ? "Ambient Atmosphere" : "Active Venting");
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    p.openLoopActive = false;
                    if (p.atmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopVentilation) continue;

                    if (!p.atmosphericControlEnabled)
                    {
                        continue;
                    }
                    if (!IsOpenLoopEnvironmentUsable(ctx))
                    {
                        continue;
                    }

                    int partCapacity = Math.Max(p.capacity, 1);

                    double ecReq = Math.Max(p.atmosphericControlECRate, 0) * deltaTime * partCapacity * ctx.occupancyScale;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecResult.ecRatio < 0.99)
                    {
                        continue;
                    }

                    status.activeOpenLoopVentCapacity += partCapacity;
                    p.openLoopActive = true;
                }
            }

        }

        /// <summary>Removes cabin CO2 through all active atmospheric-control systems.</summary>
        void RunScrubber(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime,
            double retainedCO2Floor = 0)
        {
            double totalRegenerativeRemoved = 0;
            double totalLiOHRemoved = 0;
            double totalVentedRemoved = 0;
            int activeLiOHCount = 0;
            List<ScrubberContribution> contributions = ctx.scrubberContributions;
            contributions.Clear();

            // Rates per seat
            double baseScrubRate = scrubberRequestRate * deltaTime;
            double availableCO2 =
                Math.Max(status.cabinCO2 - Math.Max(retainedCO2Floor, 0), 0);
            double lithiumHydroxidePerCO2 = scrubberRequestRate > epsilon ? lithiumHydroxideRequestRate / scrubberRequestRate : 0;
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

                    if (m.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                    {
                        int elsCapacity = Math.Max(m.part.CrewCapacity, 1);
                        if (m.openLoopVentingActive)
                        {
                            contributions.Add(new ScrubberContribution
                            {
                                module = m,
                                mode = KickLifeSupportModule.AtmosphereControlOpenLoopVentilation,
                                capacity = elsCapacity,
                                loaded = true,
                                usesOpenLoopOxygenAssist = !ctx.ambientSafe,
                                openLoopOxygenMultiplier = m.openLoopOxygenMultiplier
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

                    double ecReq = Math.Max(m.atmosphericControlECRate, 0) *
                        deltaTime * partCapacity * ctx.occupancyScale;

                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    m.currentAtmosphericControlECRate = deltaTime > epsilon
                        ? ecRes.amountConsumed / deltaTime
                        : 0;

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

                    if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                    {
                        if (p.openLoopActive)
                        {
                            contributions.Add(new ScrubberContribution
                            {
                                protoPart = p,
                                mode = KickLifeSupportModule.AtmosphereControlOpenLoopVentilation,
                                capacity = partCapacity,
                                loaded = false,
                                usesOpenLoopOxygenAssist = !ctx.ambientSafe,
                                openLoopOxygenMultiplier = p.openLoopOxygenMultiplier
                            });
                        }
                        continue;
                    }

                    if (!isScrubberOn) continue;

                    double ecReq = Math.Max(p.atmosphericControlECRate, 0) *
                        deltaTime * partCapacity * ctx.occupancyScale;
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
                        totalRegenerativeRemoved += actualScrubAmount;
                        ProduceResource(ctx.vessel, co2Id, actualScrubAmount);
                        if (contribution.module != null) contribution.module.SetAtmosphericControlStatus("Active");
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
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
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                    {
                        if (contribution.usesOpenLoopOxygenAssist)
                        {
                            double o2Req =
                                requestedScrubAmount *
                                GetOpenLoopExtraOxygenPerCO2(
                                    contribution.openLoopOxygenMultiplier);
                            ConsumeResource(ctx.vessel, o2Id, o2Req);
                            if (contribution.module != null)
                                contribution.module.SetAtmosphericControlStatus("Active Venting");
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
                        contribution.module.SetAtmosphericControlStatus("Active");
                    }
                    else if (contribution.usesOpenLoopOxygenAssist)
                    {
                        contribution.module.SetAtmosphericControlStatus("Active Venting");
                    }
                }
            }

            status.cabinCO2 -= (float)(totalRegenerativeRemoved + totalLiOHRemoved + totalVentedRemoved);
            status.lastLiOHScrubAmount = totalLiOHRemoved;
            status.lastOpenLoopVentedAmount = totalVentedRemoved;
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
        double CalculateCabinCO2(LifeSupportStatus status, double cabinAirVolume)
        {
            if (double.IsNaN(status.cabinCO2) || double.IsInfinity(status.cabinCO2))
            {
                status.cabinCO2 = 0;
            }

            if (status.cabinCO2 < 0) status.cabinCO2 = 0;
            if (cabinAirVolume > 0)
                return status.cabinCO2 / cabinAirVolume;
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
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    if (p.capacity == 0) continue;

                    if (!p.thermalControlEnabled)
                    {
                        if (p.crew > 0)
                            climateFailureDetected = true;
                        continue;
                    }

                    double ecReq = p.thermalControlECRate * p.capacity;
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

            foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
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
            double co2Level = CalculateCabinCO2(status, ctx.cabinAirVolume);

            // PRIORITY 0: Nominal
            status.lsStatus = "Nominal";

            if (co2Level >= co2Fatal && status.lowO2Time >= graceO2)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Crew on '{ctx.vessel.vesselName}' suffocated (CO2). Time unable to breathe: {status.lowO2Time:F1}s (Limit: {graceO2:F1}s); Level: {co2Level:P2}");
                KillCrew(ctx.vessel, "Crew lost to CO2 poisoning", $"Carbon dioxide concentration aboard {ctx.vessel.vesselName} reached fatal levels.");
                status.lsStatus = $"CO2 High ({FormatRemainingTime(0)})";
                return;
            }

            if (status.ambientExposureRemaining == 0 && status.ambientExposureTime > 0)
            {
                Debug.LogWarning($"[KICKLS] DEATH: Environment-exposed crew on '{ctx.vessel.vesselName}' died. Longest exposure: {status.ambientExposureTime:F1}s");
                KillAmbientDependentCrew(ctx, "Crew lost to cabin pressure failure", $"Crew in unpressurized or depressurized compartments aboard {ctx.vessel.vesselName} were exposed to an unsurvivable environment.");
                status.lsStatus = $"Depressurized ({FormatRemainingTime(0)})";
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
        (double resourceBProduced, double resourceARatio) ProcessConsumption(
            Vessel v,
            double deltaTime,
            int resourceA,
            float resourceARate,
            int resourceB,
            float resourceBRate,
            int crewCount,
            bool store = true)
        {
            double rARequestRate = resourceARate * deltaTime * crewCount;
            if (rARequestRate <= epsilon) return (0, 1.0);

            (double consumed, double ratio) resultA = ConsumeResource(v, resourceA, rARequestRate);

            double rBProduced =
                resultA.ratio *
                resourceBRate *
                deltaTime *
                crewCount;
            if (store)
            {
                ProduceResource(v, resourceB, rBProduced);
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

        double GetAtmosphericControlECRate(
            ProtoPartModuleSnapshot module,
            int atmosphereControlMode,
            KickLifeSupportModule prefabModule)
        {
            string value = module.moduleValues.GetValue("atmosphericControlECRate");
            if (value != null &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double configuredRate))
            {
                return Math.Max(configuredRate, 0);
            }

            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                return prefabModule != null
                    ? prefabModule.openLoopAtmosphericControlECRate
                    : 0.005;
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlRegenerativeScrubber)
                return prefabModule != null
                    ? prefabModule.zeoliteAtmosphericControlECRate
                    : 0.2;
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlSolidAmine)
                return prefabModule != null
                    ? prefabModule.solidAmineAtmosphericControlECRate
                    : 0.1;
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlLiOH)
                return prefabModule != null
                    ? prefabModule.liohAtmosphericControlECRate
                    : 0.05;
            return 0;
        }

        KickLifeSupportModule GetPrefabLifeSupportModule(ProtoPartSnapshot part)
        {
            return part != null &&
                   part.partInfo != null &&
                   part.partInfo.partPrefab != null
                ? part.partInfo.partPrefab.FindModuleImplementing<KickLifeSupportModule>()
                : null;
        }

        double GetModuleDouble(ProtoPartModuleSnapshot module, string fieldName, double defaultValue = 0)
        {
            string value = module.moduleValues.GetValue(fieldName);
            return value != null &&
                   double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : defaultValue;
        }

        bool GetModuleBool(ProtoPartModuleSnapshot module, string fieldName, bool defaultValue)
        {
            string value = module.moduleValues.GetValue(fieldName);
            return value != null && bool.TryParse(value, out bool parsed)
                ? parsed
                : defaultValue;
        }

        bool IsRegenerativeScrubber(int atmosphereControlMode)
        {
            return atmosphereControlMode == KickLifeSupportModule.AtmosphereControlRegenerativeScrubber ||
                   atmosphereControlMode == KickLifeSupportModule.AtmosphereControlSolidAmine;
        }

        bool IsOpenLoopEnvironmentUsable(VesselLifeSupportContext ctx)
        {
            return IsOpenLoopPressureUsable(ctx);
        }

        bool IsOpenLoopPressureUsable(VesselLifeSupportContext ctx)
        {
            if (ctx == null || ctx.vessel == null || ctx.vessel.mainBody == null) return false;
            if (ctx.underwater) return false;
            return ctx.vessel.staticPressurekPa >= OpenLoopPressureMinimumKPa;
        }

        bool IsPressureExposed(int atmosphereControlMode, VesselLifeSupportContext ctx)
        {
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                return !ctx.ambientSafe;
            if (KickLifeSupportModule.UsesOpenLoopVentilation(atmosphereControlMode))
                return !IsOpenLoopPressureUsable(ctx);
            return false;
        }

        public double GetPressureExposureGrace(int atmosphereControlMode, Vessel vessel)
        {
            if (atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone &&
                vessel != null &&
                !IsVesselUnderwater(vessel))
            {
                return vessel.staticPressurekPa < UnpressurizedPressureFailureKPa
                    ? 0
                    : graceO2;
            }

            return graceUnpressurized;
        }

        string GetOpenLoopEnvironmentStatus(VesselLifeSupportContext ctx)
        {
            if (ctx.underwater) return "Underwater";
            return "No Pressure";
        }

        bool IsAtmosphericControlEnabled(ProtoPartModuleSnapshot module)
        {
            string value = module.moduleValues.GetValue("scrubberEnabled");
            return value == null || !bool.TryParse(value, out bool enabled) || enabled;
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

            if (status.ambientExposureRemaining == 0 && status.ambientExposureTime > 0)
            {
                hazards.Add(new SituationHazard("No Ambient Air", 0));
            }
            else if (status.ambientExposureTime > 0)
            {
                hazards.Add(new SituationHazard("No Ambient Air", status.ambientExposureRemaining));
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
                "KICK Life Support: external environment unsafe",
                $"{v.vesselName}: exposed crew have {FormatRemainingTime(status.ambientExposureRemaining)} before the environment becomes fatal.");

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
                Debug.LogWarning($"[KICKLS] KillCrew executed on '{v.vesselName}'. Reason: {reportTitle}. Lost crew: {names}");
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
                    if (!IsPressureExposed(module.atmosphereControlMode, ctx) ||
                        module.pressureExposureTime <
                        GetPressureExposureGrace(module.atmosphereControlMode, ctx.vessel))
                        continue;

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
                    if (!IsPressureExposed(record.atmosphereControlMode, ctx) ||
                        record.pressureExposureTime <
                        GetPressureExposureGrace(record.atmosphereControlMode, ctx.vessel))
                        continue;

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
                Debug.LogWarning($"[KICKLS] KillAmbientDependentCrew executed on '{ctx.vessel.vesselName}'. Reason: {reportTitle}. Lost crew: {names}");
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

            foreach (ProtoPartResourceSnapshot resource in podPart.resources)
            {
                if (resource.definition.id == lithiumHydroxideId) liohRes = resource;
                if (resource.definition.id == wasteId) wasteRes = resource;
            }

            if (liohRes == null) return false;

            double liOHToAdd = liohRes.maxAmount - liohRes.amount;
            if (liOHToAdd < liohRes.maxAmount * 0.1) return false;

            double wasteToStore = cartridgeVolume - liOHToAdd;

            if (wasteRes != null)
            {
                if ((wasteRes.maxAmount - wasteRes.amount) < wasteToStore)
                {
                    return false;
                }
            }

            foreach (ProtoPartSnapshot p in v.protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartModuleSnapshot m in p.modules)
                {
                    if (m.moduleName == "ModuleInventoryPart")
                    {
                        ConfigNode inventoryNode = m.moduleValues.GetNode("STOREDPARTS");
                        if (inventoryNode == null) continue;
                        ConfigNode[] storedParts = inventoryNode.GetNodes("STOREDPART");
                        for (int i = 0; i < storedParts.Length; i++)
                        {
                            ConfigNode itemNode = storedParts[i];
                            if (itemNode.GetValue("partName") == cartridgePartName)
                            {
                                int quantity = 1;
                                if (itemNode.HasValue("quantity"))
                                    int.TryParse(itemNode.GetValue("quantity"), out quantity);

                                if (quantity > 1)
                                {
                                    quantity--;
                                    itemNode.SetValue("quantity", quantity.ToString());
                                }
                                else
                                {
                                    m.moduleValues.RemoveNode(itemNode);
                                }

                                liohRes.amount = liohRes.maxAmount;

                                if (wasteRes != null)
                                {
                                    wasteRes.amount += wasteToStore;
                                    if (wasteRes.amount > wasteRes.maxAmount) wasteRes.amount = wasteRes.maxAmount;
                                }
                                else
                                {
                                    ProduceResource(v, wasteId, wasteToStore);
                                }
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        #endregion

    }
}

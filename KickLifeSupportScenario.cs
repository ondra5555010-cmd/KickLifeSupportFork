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

        public Dictionary<Guid, LifeSupportStatus> database = new Dictionary<Guid, LifeSupportStatus>();
        readonly Dictionary<Guid, CachedVesselContext> vesselContextCache =
            new Dictionary<Guid, CachedVesselContext>();

        class VesselLifeSupportContext
        {
            public Vessel vessel;
            public bool loaded;
            public int liveCrew;
            public int cabinCrew;
            public int cabinOxygenCrew;
            public int cabinCapacity;
            public double cabinAirVolume;
            public float cabinCO2;
            public bool ambientSafe;
            public bool underwater;
            public double occupancyScale;
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
            public double atmosphericControlHeatPerEC;
            public double airVolumePerSeat;
            public double pressureMinimumKPa;
            public double oxygenWastePerCO2Removed;
            public bool canUseAmbient;
            public bool retainsCO2;
            public bool useMaxCapacity;
            public bool openLoopActive;
            public double pressureExposureTime;
            public double lowO2Time;
            public double lowWaterTime;
            public double lowFoodTime;
            public double lowClimateTime;
            public double tempRangeTime;
            public bool thermalControlEnabled;
            public double thermalControlECRate;
            public double cabinTemp;
        }

        struct ScrubberContribution
        {
            public KickLifeSupportModule module;
            public ProtoLifeSupportPart protoPart;
            public int mode;
            public double activeCapacity;
            public double oxygenWastePerCO2Removed;
            public bool loaded;
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
                    ResetCabinHazardState(ctx);
                    ResetAtmosphericControlRuntime(ctx);
                    if (ctx.cabinCO2 > epsilon)
                    {
                        RunScrubber(ctx, data, deltaTime);
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
                EatFood(ctx, deltaTime);
                DrinkWater(ctx, deltaTime);
                MonitorTemperature(ctx, data, deltaTime);
                RefreshHazardSummary(ctx, data);

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
            ctx.cabinOxygenCrew = 0;
            ctx.cabinCapacity = 0;
            ctx.cabinAirVolume = 0;
            ctx.cabinCO2 = 0;
            ctx.ambientSafe = IsAmbientAtmosphereSafe(vessel);
            ctx.underwater = IsVesselUnderwater(vessel);
            ctx.occupancyScale = 0;
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
                        lifeSupport.canUseAmbient,
                        lifeSupport.retainsCO2,
                        crew,
                        lifeSupport.part.CrewCapacity,
                        lifeSupport.airVolumePerSeat,
                        lifeSupport.pressureMinimumKPa,
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
                        record.canUseAmbient,
                        record.retainsCO2,
                        record.crew,
                        record.capacity,
                        record.airVolumePerSeat,
                        record.pressureMinimumKPa,
                        cabinCO2);
                }
            }

            if (ctx.loaded && ctx.lifeSupportModules.Count == 0) return null;
            if (!ctx.loaded && ctx.protoLifeSupportParts.Count == 0) return null;

            double usableScrubberCapacity = GetUsableScrubberCapacity(ctx);
            if (ctx.liveCrew == 0)
            {
                ctx.occupancyScale =
                    usableScrubberCapacity > 0 && ctx.cabinCO2 > epsilon
                        ? Math.Min(1.0 / usableScrubberCapacity, 1.0)
                        : 0.0;
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

        void ResetCabinHazardState(VesselLifeSupportContext ctx)
        {
            if (ctx == null) return;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module == null) continue;
                    if (module.part != null && module.part.protoModuleCrew.Count > 0) continue;
                    ResetCabinHazardTimers(module);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (part == null) continue;
                    if (part.crew > 0) continue;
                    ResetCabinHazardTimers(part);
                }
            }
        }

        void RefreshHazardSummary(VesselLifeSupportContext ctx, LifeSupportStatus status)
        {
            if (ctx == null || status == null) return;

            status.lowO2Time = 0;
            status.lowWaterTime = 0;
            status.lowFoodTime = 0;
            status.lowClimateTime = 0;
            status.tempRangeTime = 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module == null || module.part == null || module.part.protoModuleCrew.Count == 0)
                        continue;

                    status.lowO2Time = Math.Max(status.lowO2Time, module.lowO2Time);
                    status.lowWaterTime = Math.Max(status.lowWaterTime, module.lowWaterTime);
                    status.lowFoodTime = Math.Max(status.lowFoodTime, module.lowFoodTime);
                    status.lowClimateTime = Math.Max(status.lowClimateTime, module.lowClimateTime);
                    status.tempRangeTime = Math.Max(status.tempRangeTime, module.tempRangeTime);

                    if (module.tempRangeTime > 0)
                    {
                        status.lastCabinTemp = module.cabinTemp;
                    }
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (part == null || part.crew == 0) continue;

                    status.lowO2Time = Math.Max(status.lowO2Time, part.lowO2Time);
                    status.lowWaterTime = Math.Max(status.lowWaterTime, part.lowWaterTime);
                    status.lowFoodTime = Math.Max(status.lowFoodTime, part.lowFoodTime);
                    status.lowClimateTime = Math.Max(status.lowClimateTime, part.lowClimateTime);
                    status.tempRangeTime = Math.Max(status.tempRangeTime, part.tempRangeTime);

                    if (part.tempRangeTime > 0)
                    {
                        status.lastCabinTemp = part.cabinTemp;
                    }
                }
            }
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
            record.atmosphericControlEnabled = IsAtmosphericControlEnabled(record.module);
            KickLifeSupportModule prefabModule = GetPrefabLifeSupportModule(record.part);
            record.atmosphericControlECRate =
                GetAtmosphericControlECRate(
                    record.module,
                    prefabModule);
            record.atmosphericControlHeatPerEC =
                GetModuleDouble(
                    record.module,
                    "atmosphericControlHeatPerEC",
                    prefabModule != null ? prefabModule.atmosphericControlHeatPerEC : 1);
            record.airVolumePerSeat =
                GetModuleDouble(
                    record.module,
                    "airVolumePerSeat",
                    prefabModule != null ? prefabModule.airVolumePerSeat : 2000);
            record.oxygenWastePerCO2Removed =
                GetModuleDouble(
                    record.module,
                    "oxygenWastePerCO2Removed",
                    prefabModule != null ? prefabModule.oxygenWastePerCO2Removed : 0);
            record.pressureMinimumKPa =
                GetModuleDouble(
                    record.module,
                    "pressureMinimumKPa",
                    prefabModule != null ? prefabModule.pressureMinimumKPa : 0);
            record.canUseAmbient =
                GetModuleBool(
                    record.module,
                    "canUseAmbient",
                    prefabModule != null && prefabModule.canUseAmbient);
            record.retainsCO2 =
                GetModuleBool(
                    record.module,
                    "retainsCO2",
                    prefabModule == null || prefabModule.retainsCO2);
            record.useMaxCapacity =
                GetModuleBool(
                    record.module,
                    "useMaxCapacity",
                    prefabModule != null && prefabModule.useMaxCapacity);
            record.atmosphereControlMode =
                GetEffectiveAtmosphereControlMode(
                    GetAtmosphereControlMode(record.module),
                    record.retainsCO2);
            record.openLoopActive = false;
            record.pressureExposureTime = GetCabinPressureExposureTime(record);
            record.lowO2Time = GetModuleDouble(record.module, "lowO2Time");
            record.lowWaterTime = GetModuleDouble(record.module, "lowWaterTime");
            record.lowFoodTime = GetModuleDouble(record.module, "lowFoodTime");
            record.lowClimateTime = GetModuleDouble(record.module, "lowClimateTime");
            record.tempRangeTime = GetModuleDouble(record.module, "tempRangeTime");
            record.thermalControlEnabled =
                GetModuleBool(record.module, "climateControlEnabled", true);
            record.thermalControlECRate =
                GetModuleDouble(record.module, "systemECRate", 0.003);
            record.cabinTemp = GetModuleDouble(record.module, "cabinTemp", 22);
        }

        void AccumulateCabinContext(
            VesselLifeSupportContext ctx,
            bool canUseAmbient,
            bool retainsCO2,
            int crew,
            int capacity,
            double airVolumePerSeat,
            double pressureMinimumKPa,
            float cabinCO2)
        {
            if (!IsPressureSupported(ctx, pressureMinimumKPa))
            {
                return;
            }

            if (!retainsCO2) return;
            ctx.cabinCapacity += capacity;
            ctx.cabinAirVolume += capacity * Math.Max(airVolumePerSeat, 0);
            ctx.cabinCO2 += cabinCO2;
            ctx.cabinCrew += crew;
            if (!(canUseAmbient && ctx.ambientSafe))
            {
                ctx.cabinOxygenCrew += crew;
            }
        }

        double GetUsableScrubberCapacity(VesselLifeSupportContext ctx)
        {
            double capacity = 0;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (!module.retainsCO2 ||
                        !module.scrubberEnabled ||
                        !UsesPoweredAtmosphericControl(module.atmosphereControlMode) ||
                        !IsPressureSupported(ctx, module))
                    {
                        continue;
                    }

                    capacity += Math.Max(module.part.CrewCapacity, 1);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (!part.retainsCO2 ||
                        !part.atmosphericControlEnabled ||
                        !UsesPoweredAtmosphericControl(part.atmosphereControlMode) ||
                        !IsPressureSupported(ctx, part))
                    {
                        continue;
                    }

                    capacity += Math.Max(part.capacity, 1);
                }
            }

            return capacity;
        }

        bool UsesSharedBreathableCabin(KickLifeSupportModule module, VesselLifeSupportContext ctx)
        {
            if (module == null || module.part == null || module.part.protoModuleCrew.Count == 0)
                return false;

            return module.retainsCO2 && IsPressureSupported(ctx, module);
        }

        bool UsesSharedBreathableCabin(ProtoLifeSupportPart part, VesselLifeSupportContext ctx)
        {
            if (part == null || part.crew == 0) return false;

            return part.retainsCO2 && IsPressureSupported(ctx, part);
        }

        bool UsesPoweredAtmosphericControl(int atmosphereControlMode)
        {
            return atmosphereControlMode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation ||
                   atmosphereControlMode == KickLifeSupportModule.AtmosphereControlLiOH ||
                   IsRegenerativeScrubber(atmosphereControlMode);
        }

        int GetEffectiveAtmosphereControlMode(int atmosphereControlMode, bool retainsCO2)
        {
            return KickLifeSupportModule.GetEffectiveAtmosphereControlMode(
                atmosphereControlMode,
                retainsCO2);
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
                    if (!m.retainsCO2) continue;
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
                            if (!GetModuleBool(m, "retainsCO2", true)) continue;
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
                    if (!m.retainsCO2 || !IsPressureSupported(ctx, m))
                    {
                        m.cabinCO2 = 0;
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
                    if (!p.retainsCO2 || !IsPressureSupported(ctx, p))
                    {
                        p.module.moduleValues.SetValue("cabinCO2", 0);
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
                module.currentAtmosphericOxygenWasteRate = 0;
                module.openLoopVentingActive = false;
            }
        }

        double GetActiveScrubberCapacity(
            VesselLifeSupportContext ctx,
            int installedCapacity,
            bool useMaxCapacity)
        {
            double nominalCapacity =
                Math.Max(installedCapacity, 0) * Math.Max(ctx.occupancyScale, 0);
            return useMaxCapacity && installedCapacity > 0
                ? installedCapacity
                : nominalCapacity;
        }

        void UpdatePressureExposure(VesselLifeSupportContext ctx, LifeSupportStatus status, double deltaTime)
        {
            double longestCrewExposure = 0;
            double shortestRemaining = double.MaxValue;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    bool exposed = IsPressureExposed(module, ctx);
                    double exposureTime = exposed
                        ? Math.Max(GetCabinPressureExposureTime(module) + deltaTime, 0)
                        : 0;
                    SetCabinPressureExposureTime(module, exposureTime);

                    if (exposed &&
                        !module.retainsCO2 &&
                        exposureTime >= graceUnpressurized)
                    {
                        module.cabinCO2 = 0;
                    }

                    if (exposed && module.part.protoModuleCrew.Count > 0)
                    {
                        longestCrewExposure = Math.Max(longestCrewExposure, exposureTime);
                            shortestRemaining = Math.Min(
                            shortestRemaining,
                            GetPressureExposureGrace(module, ctx.vessel) -
                            exposureTime);
                    }
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    bool exposed = IsPressureExposed(part, ctx);
                    double exposureTime = exposed
                        ? Math.Max(GetCabinPressureExposureTime(part) + deltaTime, 0)
                        : 0;
                    SetCabinPressureExposureTime(part, exposureTime);

                    if (exposed &&
                        !part.retainsCO2 &&
                        exposureTime >= graceUnpressurized)
                    {
                        part.module.moduleValues.SetValue("cabinCO2", "0");
                    }

                    if (exposed && part.crew > 0)
                    {
                        longestCrewExposure = Math.Max(longestCrewExposure, exposureTime);
                            shortestRemaining = Math.Min(
                            shortestRemaining,
                            GetPressureExposureGrace(part, ctx.vessel) -
                            exposureTime);
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

            double baselineO2Request = o2RequestRate * deltaTime * ctx.cabinOxygenCrew;
            (double _, double baselineO2Ratio) =
                ConsumeResource(ctx.vessel, o2Id, baselineO2Request);

            double cabinCO2Produced = co2RequestRate * deltaTime * ctx.cabinCrew;
            status.cabinCO2 += (float)cabinCO2Produced;

            bool oxygenFailure = baselineO2Ratio < 0.99;
            double co2Level = CalculateCabinCO2(status, ctx.cabinAirVolume);
            bool breathableAtmosphereFailing =
                oxygenFailure || co2Level >= co2Fatal;

            UpdateCrewHazardTimer(
                ctx,
                "lowO2Time",
                breathableAtmosphereFailing ? deltaTime : 0,
                module => breathableAtmosphereFailing && UsesSharedBreathableCabin(module, ctx),
                part => breathableAtmosphereFailing && UsesSharedBreathableCabin(part, ctx));
            ProcessNonRetainedBreathing(ctx, status, deltaTime);
        }

        void ProcessOpenLoopVentilation(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime)
        {
            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule m in ctx.lifeSupportModules)
                {
                    int effectiveAtmosphereControlMode =
                        GetEffectiveAtmosphereControlMode(
                            m.atmosphereControlMode,
                            m.retainsCO2);
                    if (effectiveAtmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopVentilation) continue;

                    m.openLoopVentingActive = false;
                    int crew = m.part != null ? m.part.protoModuleCrew.Count : 0;

                    if (!IsOpenLoopEnvironmentUsable(ctx, m))
                    {
                        m.SetAtmosphericControlStatus(GetOpenLoopEnvironmentStatus(ctx));
                        continue;
                    }

                    if (!m.scrubberEnabled || crew <= 0)
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    double ecReq = Math.Max(m.atmosphericControlECRate, 0) * deltaTime * crew;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    m.currentAtmosphericControlECRate = deltaTime > epsilon
                        ? ecResult.ecConsumed / deltaTime
                        : 0;
                    if (ecResult.ecRatio < 0.99)
                    {
                        m.SetAtmosphericControlStatus("No EC");
                        continue;
                    }

                    m.openLoopVentingActive = true;
                    m.SetAtmosphericControlStatus(
                        UsesAmbientBreathing(ctx, m)
                            ? "Ambient Atmosphere"
                            : "Active Venting");
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    p.openLoopActive = false;
                    int effectiveAtmosphereControlMode =
                        GetEffectiveAtmosphereControlMode(
                            p.atmosphereControlMode,
                            p.retainsCO2);
                    if (effectiveAtmosphereControlMode != KickLifeSupportModule.AtmosphereControlOpenLoopVentilation) continue;
                    if (!IsOpenLoopEnvironmentUsable(ctx, p))
                    {
                        continue;
                    }

                    int crew = Math.Max(p.crew, 0);
                    if (!p.atmosphericControlEnabled || crew <= 0)
                    {
                        continue;
                    }

                    double ecReq = Math.Max(p.atmosphericControlECRate, 0) * deltaTime * crew;
                    (double ecConsumed, double ecRatio) ecResult = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (ecResult.ecRatio < 0.99)
                    {
                        continue;
                    }
                    p.openLoopActive = true;
                }
            }

        }

        void ProcessNonRetainedBreathing(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime)
        {
            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module == null || module.part == null || module.part.protoModuleCrew.Count == 0)
                        continue;
                    if (module.retainsCO2) continue;

                    if (IsPressureExposed(module, ctx))
                    {
                        SetCabinHazardTimer(module, "lowO2Time", 0);
                        continue;
                    }

                    if (UsesAmbientBreathing(ctx, module))
                    {
                        SetCabinHazardTimer(module, "lowO2Time", 0);
                        continue;
                    }

                    if (KickLifeSupportModule.UsesOpenLoopVentilation(module.atmosphereControlMode) &&
                        module.openLoopVentingActive)
                    {
                        ProcessOpenLoopBreathing(ctx, status, deltaTime, module);
                        continue;
                    }

                    SetCabinHazardTimer(
                        module,
                        "lowO2Time",
                        GetCabinHazardTimer(module, "lowO2Time") + deltaTime);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (part == null || part.crew == 0) continue;
                    if (part.retainsCO2) continue;

                    if (IsPressureExposed(part, ctx))
                    {
                        SetCabinHazardTimer(part, "lowO2Time", 0);
                        continue;
                    }

                    if (UsesAmbientBreathing(ctx, part))
                    {
                        SetCabinHazardTimer(part, "lowO2Time", 0);
                        continue;
                    }

                    if (KickLifeSupportModule.UsesOpenLoopVentilation(part.atmosphereControlMode) &&
                        part.openLoopActive)
                    {
                        ProcessOpenLoopBreathing(ctx, status, deltaTime, part);
                        continue;
                    }

                    SetCabinHazardTimer(
                        part,
                        "lowO2Time",
                        GetCabinHazardTimer(part, "lowO2Time") + deltaTime);
                }
            }
        }

        void ProcessOpenLoopBreathing(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime,
            KickLifeSupportModule module)
        {
            int crew = module != null && module.part != null
                ? module.part.protoModuleCrew.Count
                : 0;
            if (crew <= 0)
            {
                SetCabinHazardTimer(module, "lowO2Time", 0);
                return;
            }

            double baselineO2Request = o2RequestRate * deltaTime * crew;
            (double o2Consumed, double o2Ratio) =
                ConsumeResource(ctx.vessel, o2Id, baselineO2Request);
            SetCabinHazardTimer(
                module,
                "lowO2Time",
                o2Ratio < 0.99
                    ? GetCabinHazardTimer(module, "lowO2Time") + deltaTime
                    : 0);
        }

        void ProcessOpenLoopBreathing(
            VesselLifeSupportContext ctx,
            LifeSupportStatus status,
            double deltaTime,
            ProtoLifeSupportPart part)
        {
            int crew = part != null ? part.crew : 0;
            if (crew <= 0)
            {
                SetCabinHazardTimer(part, "lowO2Time", 0);
                return;
            }

            double baselineO2Request = o2RequestRate * deltaTime * crew;
            (double o2Consumed, double o2Ratio) =
                ConsumeResource(ctx.vessel, o2Id, baselineO2Request);
            SetCabinHazardTimer(
                part,
                "lowO2Time",
                o2Ratio < 0.99
                    ? GetCabinHazardTimer(part, "lowO2Time") + deltaTime
                    : 0);
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
            double totalOpenLoopRemoved = 0;
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
                    int effectiveAtmosphereControlMode =
                        GetEffectiveAtmosphereControlMode(
                            m.atmosphereControlMode,
                            m.retainsCO2);

                    if (effectiveAtmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
                    {
                        m.SetAtmosphericControlStatus(
                            m.canUseAmbient
                                ? GetAmbientAtmosphereStatus(ctx)
                                : "Inactive");
                        continue;
                    }

                    if (!m.retainsCO2 || !IsPressureSupported(ctx, m))
                    {
                        continue;
                    }

                    if (!UsesPoweredAtmosphericControl(effectiveAtmosphereControlMode))
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    if (!m.scrubberEnabled)
                    {
                        m.SetAtmosphericControlStatus("Inactive");
                        continue;
                    }

                    int partCapacity = m.part.CrewCapacity;
                    if (partCapacity == 0) partCapacity = 1;
                    double activeCapacity = GetActiveScrubberCapacity(
                        ctx,
                        partCapacity,
                        m.useMaxCapacity);

                    double ecReq = Math.Max(m.atmosphericControlECRate, 0) *
                        deltaTime * activeCapacity;

                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    m.currentAtmosphericControlECRate = deltaTime > epsilon
                        ? ecRes.amountConsumed / deltaTime
                        : 0;

                    if (activeCapacity > epsilon && ecRes.ratio < 0.99)
                    {
                        m.SetAtmosphericControlStatus("No EC");
                        continue;
                    }

                    contributions.Add(new ScrubberContribution
                    {
                        module = m,
                        mode = effectiveAtmosphereControlMode,
                        activeCapacity = activeCapacity,
                        oxygenWastePerCO2Removed =
                            UsesAmbientBreathing(ctx, m)
                                ? 0
                                : Math.Max(m.oxygenWastePerCO2Removed, 0),
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

                    if (!p.retainsCO2 || !IsPressureSupported(ctx, p))
                    {
                        continue;
                    }

                    if (!UsesPoweredAtmosphericControl(atmosphereControlMode))
                    {
                        continue;
                    }

                    if (!isScrubberOn) continue;
                    double activeCapacity = GetActiveScrubberCapacity(
                        ctx,
                        partCapacity,
                        p.useMaxCapacity);

                    double ecReq = Math.Max(p.atmosphericControlECRate, 0) *
                        deltaTime * activeCapacity;
                    (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                    if (activeCapacity > epsilon && ecRes.ratio < 0.99) continue;

                    contributions.Add(new ScrubberContribution
                    {
                        protoPart = p,
                        mode = atmosphereControlMode,
                        activeCapacity = activeCapacity,
                        oxygenWastePerCO2Removed =
                            UsesAmbientBreathing(ctx, p)
                                ? 0
                                : Math.Max(p.oxygenWastePerCO2Removed, 0),
                        loaded = false
                    });
                }
            }

            double totalActiveCapacity = 0;
            foreach (ScrubberContribution contribution in contributions)
            {
                totalActiveCapacity += Math.Max(contribution.activeCapacity, 0);
            }

            if (totalActiveCapacity > epsilon && availableCO2 > epsilon)
            {
                double totalScrubRequest = Math.Min(
                    availableCO2,
                    baseScrubRate * totalActiveCapacity);

                foreach (ScrubberContribution contribution in contributions)
                {
                    double requestedScrubAmount = contribution.activeCapacity > epsilon
                        ? totalScrubRequest * contribution.activeCapacity / totalActiveCapacity
                        : 0;
                    double actualScrubAmount = requestedScrubAmount;
                    double oxygenWasteRate = Math.Max(contribution.oxygenWastePerCO2Removed, 0);

                    if (IsRegenerativeScrubber(contribution.mode))
                    {
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
                        double liohReq = requestedScrubAmount * lithiumHydroxidePerCO2;
                        double liohTaken = contribution.loaded
                            ? ConsumeLoadedLiOH(contribution.module, liohReq)
                            : ConsumeProtoLiOH(ctx.vessel, contribution.protoPart, liohReq);

                        actualScrubAmount = lithiumHydroxidePerCO2 > epsilon ? Math.Min(requestedScrubAmount, liohTaken / lithiumHydroxidePerCO2) : 0;
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

                    if (oxygenWasteRate > epsilon && actualScrubAmount > epsilon)
                    {
                        double oxygenRequest = actualScrubAmount * oxygenWasteRate;
                        (double oxygenConsumed, double _) =
                            ConsumeResource(ctx.vessel, o2Id, oxygenRequest);
                        if (oxygenConsumed < oxygenRequest - epsilon)
                        {
                            actualScrubAmount =
                                oxygenWasteRate > epsilon
                                    ? Math.Min(actualScrubAmount, oxygenConsumed / oxygenWasteRate)
                                    : actualScrubAmount;
                            if (contribution.module != null)
                            {
                                contribution.module.SetAtmosphericControlStatus(
                                    oxygenConsumed > epsilon ? "O2 Limited" : "No O2");
                            }
                        }

                        if (contribution.module != null && deltaTime > epsilon)
                        {
                            contribution.module.currentAtmosphericOxygenWasteRate +=
                                Math.Min(oxygenConsumed, actualScrubAmount * oxygenWasteRate) /
                                deltaTime;
                        }
                    }

                    if (IsRegenerativeScrubber(contribution.mode))
                    {
                        totalRegenerativeRemoved += actualScrubAmount;
                        ProduceResource(ctx.vessel, co2Id, actualScrubAmount);
                        if (contribution.module != null &&
                            contribution.module.rawScrubberStatus != "O2 Limited" &&
                            contribution.module.rawScrubberStatus != "No O2")
                        {
                            contribution.module.SetAtmosphericControlStatus("Active");
                        }
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
                        totalLiOHRemoved += actualScrubAmount;
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                    {
                        totalOpenLoopRemoved += actualScrubAmount;
                        if (contribution.module != null &&
                            contribution.module.rawScrubberStatus != "O2 Limited" &&
                            contribution.module.rawScrubberStatus != "No O2")
                        {
                            contribution.module.SetAtmosphericControlStatus(
                                UsesAmbientBreathing(ctx, contribution.module)
                                    ? "Ambient Atmosphere"
                                    : "Active Venting");
                        }
                    }
                }
            }
            else if (contributions.Count > 0)
            {
                foreach (ScrubberContribution contribution in contributions)
                {
                    if (contribution.module == null) continue;

                    if (IsRegenerativeScrubber(contribution.mode) ||
                        contribution.mode == KickLifeSupportModule.AtmosphereControlLiOH)
                    {
                        contribution.module.SetAtmosphericControlStatus("Active");
                    }
                    else if (contribution.mode == KickLifeSupportModule.AtmosphereControlOpenLoopVentilation)
                    {
                        contribution.module.SetAtmosphericControlStatus(
                            UsesAmbientBreathing(ctx, contribution.module)
                                ? "Ambient Atmosphere"
                                : "Active Venting");
                    }
                }
            }

            status.cabinCO2 -= (float)(totalRegenerativeRemoved + totalLiOHRemoved + totalOpenLoopRemoved);
            status.lastLiOHScrubAmount = totalLiOHRemoved;
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
        void UpdateCrewHazardTimer(
            VesselLifeSupportContext ctx,
            string fieldName,
            double deltaTime,
            Func<KickLifeSupportModule, bool> loadedAffectsCrew,
            Func<ProtoLifeSupportPart, bool> unloadedAffectsCrew)
        {
            if (ctx == null) return;

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module == null || module.part == null) continue;
                    if (module.part.protoModuleCrew.Count == 0)
                    {
                        SetCabinHazardTimer(module, fieldName, 0);
                        continue;
                    }

                    bool affected = loadedAffectsCrew == null || loadedAffectsCrew(module);
                    double nextValue = affected
                        ? GetCabinHazardTimer(module, fieldName) + deltaTime
                        : 0;
                    SetCabinHazardTimer(module, fieldName, nextValue);
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (part == null) continue;
                    if (part.crew == 0)
                    {
                        SetCabinHazardTimer(part, fieldName, 0);
                        continue;
                    }

                    bool affected = unloadedAffectsCrew == null || unloadedAffectsCrew(part);
                    double nextValue = affected
                        ? GetCabinHazardTimer(part, fieldName) + deltaTime
                        : 0;
                    SetCabinHazardTimer(part, fieldName, nextValue);
                }
            }
        }

        void EatFood(VesselLifeSupportContext ctx, double deltaTime)
        {
            if (ctx == null) return;

            double ratio = ProcessConsumption(
                ctx.vessel,
                deltaTime,
                foodId,
                foodRequestRate,
                wasteId,
                wasteRequestRate,
                ctx.liveCrew,
                true).resourceARatio;

            UpdateCrewHazardTimer(
                ctx,
                "lowFoodTime",
                ratio < 0.99 ? deltaTime : 0,
                _ => ratio < 0.99,
                _ => ratio < 0.99);
        }

        /// <summary>
        /// Consumes water and makes wastewater
        /// </summary>
        /// <param name="deltaTime"></param>
        void DrinkWater(VesselLifeSupportContext ctx, double deltaTime)
        {
            if (ctx == null) return;

            double ratio = ProcessConsumption(
                ctx.vessel,
                deltaTime,
                waterId,
                waterRequestRate,
                wasteWaterId,
                wasteWaterRequestRate,
                ctx.liveCrew,
                true).resourceARatio;

            UpdateCrewHazardTimer(
                ctx,
                "lowWaterTime",
                ratio < 0.99 ? deltaTime : 0,
                _ => ratio < 0.99,
                _ => ratio < 0.99);
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

            if (!ctx.loaded)
            {
                foreach (ProtoLifeSupportPart p in ctx.protoLifeSupportParts)
                {
                    if (p == null || p.capacity == 0) continue;
                    if (p.crew == 0)
                    {
                        SetCabinHazardTimer(p, "lowClimateTime", 0);
                        continue;
                    }

                    bool climateFailure = false;
                    if (!p.thermalControlEnabled)
                    {
                        climateFailure = true;
                    }
                    else
                    {
                        double ecReq = p.thermalControlECRate * p.capacity;
                        (double amountConsumed, double ratio) ecRes = ConsumeResource(ctx.vessel, electricChargeId, ecReq);
                        if (ecRes.ratio < 0.99)
                        {
                            climateFailure = true;
                        }
                    }

                    double nextTime = climateFailure
                        ? GetCabinHazardTimer(p, "lowClimateTime") + deltaTime
                        : 0;
                    SetCabinHazardTimer(p, "lowClimateTime", nextTime);
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
                foreach (ProtoLifeSupportPart part in ctx.protoLifeSupportParts)
                {
                    if (part == null) continue;
                    SetCabinHazardTimer(part, "tempRangeTime", 0);
                }
                return;
            }

            foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
            {
                if (module == null || module.part == null) continue;
                if (module.part.protoModuleCrew.Count == 0)
                {
                    module.tempRangeTime = 0;
                    continue;
                }

                bool outOfRange =
                    module.cabinTemp < minSafeTemp || module.cabinTemp > maxSafeTemp;
                module.tempRangeTime = outOfRange
                    ? (float)Math.Max(module.tempRangeTime + deltaTime, 0)
                    : 0;
                if (outOfRange)
                {
                    status.lastCabinTemp = module.cabinTemp;
                }
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

            if (co2Level >= co2Fatal &&
                KillCrewInMatchingCabins(
                    ctx,
                    module =>
                        UsesSharedBreathableCabin(module, ctx) &&
                        module.lowO2Time >= graceO2,
                    part =>
                        UsesSharedBreathableCabin(part, ctx) &&
                        part.lowO2Time >= graceO2,
                    "Crew lost to CO2 poisoning",
                    $"Carbon dioxide concentration aboard {ctx.vessel.vesselName} reached fatal levels."))
            {
                status.lsStatus = $"CO2 High ({FormatRemainingTime(0)})";
                return;
            }

            if (status.ambientExposureRemaining == 0 && status.ambientExposureTime > 0)
            {
                KillAmbientDependentCrew(ctx, "Crew lost to cabin pressure failure", $"Crew in unpressurized or depressurized compartments aboard {ctx.vessel.vesselName} were exposed to an unsurvivable environment.");
                status.lsStatus = $"Depressurized ({FormatRemainingTime(0)})";
                status.ambientExposureTime = 0;
                return;
            }

            if (KillCrewInMatchingCabins(
                ctx,
                module =>
                    UsesSharedBreathableCabin(module, ctx) &&
                    module.lowO2Time >= graceO2,
                part =>
                    UsesSharedBreathableCabin(part, ctx) &&
                    part.lowO2Time >= graceO2,
                "Crew lost to oxygen deprivation",
                $"The crew aboard {ctx.vessel.vesselName} ran out of breathable atmosphere."))
            {
                status.lsStatus = $"No O2 ({FormatRemainingTime(0)})";
                return;
            }

            if (gameSettings.useCabinTempSystem)
            {
                if (KillCrewInMatchingCabins(
                    ctx,
                    module => module.part != null &&
                              module.part.protoModuleCrew.Count > 0 &&
                              module.lowClimateTime >= graceClimate,
                    part => part.crew > 0 &&
                            part.lowClimateTime >= graceClimate,
                    "Crew lost to atmospheric control failure",
                    $"Temperature control failed aboard {ctx.vessel.vesselName} long enough for the cabin environment to become fatal."))
                {
                    status.lsStatus = $"No O2 ({FormatRemainingTime(0)})";
                    return;
                }

                if (KillCrewInMatchingCabins(
                    ctx,
                    module => module.part != null &&
                              module.part.protoModuleCrew.Count > 0 &&
                              module.tempRangeTime >= graceTemp,
                    part => part.crew > 0 &&
                            part.tempRangeTime >= graceTemp,
                    "Crew lost to cabin temperature",
                    $"Cabin temperature aboard {ctx.vessel.vesselName} stayed outside survivable limits."))
                {
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
            if (KillCrewInMatchingCabins(
                ctx,
                module => module.part != null &&
                          module.part.protoModuleCrew.Count > 0 &&
                          module.lowWaterTime >= graceWater,
                part => part.crew > 0 &&
                        part.lowWaterTime >= graceWater,
                "Crew lost to dehydration",
                $"The crew aboard {ctx.vessel.vesselName} ran out of water."))
            {
                status.lsStatus = "Crew dehydrated!";
                return;
            }
            else if (status.lowWaterTime > 0)
            {
                status.lsStatus = $"Thirsty! ({graceWater - status.lowWaterTime:F0}s)";
                return;
            }

            // PRIORITY 4: Food (Slow Death)
            if (KillCrewInMatchingCabins(
                ctx,
                module => module.part != null &&
                          module.part.protoModuleCrew.Count > 0 &&
                          module.lowFoodTime >= graceFood,
                part => part.crew > 0 &&
                        part.lowFoodTime >= graceFood,
                "Crew lost to starvation",
                $"The crew aboard {ctx.vessel.vesselName} ran out of food."))
            {
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

            return KickLifeSupportModule.AtmosphereControlNone;
        }

        double GetAtmosphericControlECRate(
            ProtoPartModuleSnapshot module,
            KickLifeSupportModule prefabModule)
        {
            return GetModuleDouble(
                module,
                "atmosphericControlECRate",
                prefabModule != null ? prefabModule.atmosphericControlECRate : 0);
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
            return atmosphereControlMode == KickLifeSupportModule.AtmosphereControlRegenerativeScrubber;
        }

        bool UsesAmbientBreathing(VesselLifeSupportContext ctx, KickLifeSupportModule module)
        {
            return module != null &&
                   module.canUseAmbient &&
                   ctx != null &&
                   ctx.ambientSafe &&
                   IsPressureSupported(ctx, module);
        }

        bool UsesAmbientBreathing(VesselLifeSupportContext ctx, ProtoLifeSupportPart part)
        {
            return part != null &&
                   part.canUseAmbient &&
                   ctx != null &&
                   ctx.ambientSafe &&
                   IsPressureSupported(ctx, part);
        }

        bool IsOpenLoopEnvironmentUsable(VesselLifeSupportContext ctx, KickLifeSupportModule module)
        {
            return IsPressureSupported(ctx, module);
        }

        bool IsOpenLoopEnvironmentUsable(VesselLifeSupportContext ctx, ProtoLifeSupportPart part)
        {
            return IsPressureSupported(ctx, part);
        }

        bool IsPressureSupported(VesselLifeSupportContext ctx, KickLifeSupportModule module)
        {
            return IsPressureSupported(
                ctx,
                module != null ? module.pressureMinimumKPa : 0);
        }

        bool IsPressureSupported(VesselLifeSupportContext ctx, ProtoLifeSupportPart part)
        {
            return IsPressureSupported(
                ctx,
                part != null ? part.pressureMinimumKPa : 0);
        }

        bool IsPressureSupported(VesselLifeSupportContext ctx, double minimumPressure)
        {
            if (minimumPressure <= 0) return true;
            if (ctx == null || ctx.vessel == null || ctx.vessel.mainBody == null) return false;
            if (ctx.underwater) return false;
            return ctx.vessel.staticPressurekPa >= Math.Max(minimumPressure, 0);
        }

        double GetCabinPressureExposureTime(KickLifeSupportModule module)
        {
            return module != null ? Math.Max(module.pressureExposureTime, 0) : 0;
        }

        double GetCabinPressureExposureTime(ProtoLifeSupportPart part)
        {
            return part != null ? Math.Max(part.pressureExposureTime, 0) : 0;
        }

        void SetCabinPressureExposureTime(KickLifeSupportModule module, double value)
        {
            if (module == null) return;
            module.pressureExposureTime = (float)Math.Max(value, 0);
        }

        void SetCabinPressureExposureTime(ProtoLifeSupportPart part, double value)
        {
            if (part == null || part.module == null) return;
            part.pressureExposureTime = Math.Max(value, 0);
            part.module.moduleValues.SetValue(
                "pressureExposureTime",
                part.pressureExposureTime.ToString("R", CultureInfo.InvariantCulture));
        }

        double GetCabinHazardTimer(KickLifeSupportModule module, string fieldName)
        {
            if (module == null) return 0;

            switch (fieldName)
            {
                case "lowO2Time": return Math.Max(module.lowO2Time, 0);
                case "lowWaterTime": return Math.Max(module.lowWaterTime, 0);
                case "lowFoodTime": return Math.Max(module.lowFoodTime, 0);
                case "lowClimateTime": return Math.Max(module.lowClimateTime, 0);
                case "tempRangeTime": return Math.Max(module.tempRangeTime, 0);
                default: return 0;
            }
        }

        double GetCabinHazardTimer(ProtoLifeSupportPart part, string fieldName)
        {
            if (part == null) return 0;

            switch (fieldName)
            {
                case "lowO2Time": return Math.Max(part.lowO2Time, 0);
                case "lowWaterTime": return Math.Max(part.lowWaterTime, 0);
                case "lowFoodTime": return Math.Max(part.lowFoodTime, 0);
                case "lowClimateTime": return Math.Max(part.lowClimateTime, 0);
                case "tempRangeTime": return Math.Max(part.tempRangeTime, 0);
                default: return 0;
            }
        }

        void SetCabinHazardTimer(KickLifeSupportModule module, string fieldName, double value)
        {
            if (module == null) return;
            float clamped = (float)Math.Max(value, 0);

            switch (fieldName)
            {
                case "lowO2Time":
                    module.lowO2Time = clamped;
                    break;
                case "lowWaterTime":
                    module.lowWaterTime = clamped;
                    break;
                case "lowFoodTime":
                    module.lowFoodTime = clamped;
                    break;
                case "lowClimateTime":
                    module.lowClimateTime = clamped;
                    break;
                case "tempRangeTime":
                    module.tempRangeTime = clamped;
                    break;
            }
        }

        void SetCabinHazardTimer(ProtoLifeSupportPart part, string fieldName, double value)
        {
            if (part == null || part.module == null) return;
            double clamped = Math.Max(value, 0);

            switch (fieldName)
            {
                case "lowO2Time":
                    part.lowO2Time = clamped;
                    break;
                case "lowWaterTime":
                    part.lowWaterTime = clamped;
                    break;
                case "lowFoodTime":
                    part.lowFoodTime = clamped;
                    break;
                case "lowClimateTime":
                    part.lowClimateTime = clamped;
                    break;
                case "tempRangeTime":
                    part.tempRangeTime = clamped;
                    break;
                default:
                    return;
            }

            part.module.moduleValues.SetValue(
                fieldName,
                clamped.ToString("R", CultureInfo.InvariantCulture));
        }

        void ResetCabinHazardTimers(KickLifeSupportModule module)
        {
            if (module == null) return;
            module.pressureExposureTime = 0;
            module.lowO2Time = 0;
            module.lowWaterTime = 0;
            module.lowFoodTime = 0;
            module.lowClimateTime = 0;
            module.tempRangeTime = 0;
        }

        void ResetCabinHazardTimers(ProtoLifeSupportPart part)
        {
            if (part == null || part.module == null) return;
            SetCabinPressureExposureTime(part, 0);
            SetCabinHazardTimer(part, "lowO2Time", 0);
            SetCabinHazardTimer(part, "lowWaterTime", 0);
            SetCabinHazardTimer(part, "lowFoodTime", 0);
            SetCabinHazardTimer(part, "lowClimateTime", 0);
            SetCabinHazardTimer(part, "tempRangeTime", 0);
        }

        bool IsPressureExposed(KickLifeSupportModule module, VesselLifeSupportContext ctx)
        {
            if (module == null) return false;
            return module.pressureMinimumKPa > 0 &&
                   !IsPressureSupported(ctx, module);
        }

        bool IsPressureExposed(ProtoLifeSupportPart part, VesselLifeSupportContext ctx)
        {
            if (part == null) return false;
            return part.pressureMinimumKPa > 0 &&
                   !IsPressureSupported(ctx, part);
        }

        internal double GetPressureExposureGrace(KickLifeSupportModule module, Vessel vessel)
        {
            return graceUnpressurized;
        }

        double GetPressureExposureGrace(ProtoLifeSupportPart part, Vessel vessel)
        {
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

        bool KillCrewInMatchingCabins(
            VesselLifeSupportContext ctx,
            Func<KickLifeSupportModule, bool> loadedMatch,
            Func<ProtoLifeSupportPart, bool> unloadedMatch,
            string reportTitle,
            string reportBody)
        {
            if (ctx == null || ctx.vessel == null) return false;

            bool anyoneDied = false;
            List<string> lostCrew = new List<string>();

            if (ctx.loaded)
            {
                foreach (KickLifeSupportModule module in ctx.lifeSupportModules)
                {
                    if (module == null || module.part == null) continue;
                    if (loadedMatch != null && !loadedMatch(module)) continue;

                    List<ProtoCrewMember> crewList = new List<ProtoCrewMember>(module.part.protoModuleCrew);
                    foreach (ProtoCrewMember crew in crewList)
                    {
                        crew.rosterStatus = ProtoCrewMember.RosterStatus.Dead;
                        module.part.RemoveCrewmember(crew);
                        anyoneDied = true;
                        lostCrew.Add(crew.name);
                    }
                }
            }
            else
            {
                foreach (ProtoLifeSupportPart record in ctx.protoLifeSupportParts)
                {
                    if (record == null || record.part == null) continue;
                    if (unloadedMatch != null && !unloadedMatch(record)) continue;

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
                Debug.LogWarning($"[KICKLS] Crew loss on '{ctx.vessel.vesselName}'. Reason: {reportTitle}. Lost crew: {names}");
                PostCrewDeathReport(reportTitle, $"{reportBody}\n\nLost crew: {names}");
            }

            return anyoneDied;
        }

        void KillAmbientDependentCrew(VesselLifeSupportContext ctx, string reportTitle, string reportBody)
        {
            KillCrewInMatchingCabins(
                ctx,
                module =>
                    IsPressureExposed(module, ctx) &&
                    GetCabinPressureExposureTime(module) >=
                    GetPressureExposureGrace(module, ctx.vessel),
                record =>
                    IsPressureExposed(record, ctx) &&
                    GetCabinPressureExposureTime(record) >=
                    GetPressureExposureGrace(record, ctx.vessel),
                reportTitle,
                reportBody);
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

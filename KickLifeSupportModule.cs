using System;
using UnityEngine;

namespace KickLifeSupport
{
    public partial class KickLifeSupportModule : PartModule
    {
        #region Persistent Fields
        [KSPField(isPersistant = true)]
        public float cabinCO2 = 0f;
        #endregion

        #region Resource IDs
        int wasteId = -1;
        int liohId = -1;
        int ecId = -1;
        int o2Id = -1;
        #endregion

        public const int AtmosphereControlNone = 0;
        public const int AtmosphereControlOpenLoopVentilation = 1;
        public const int AtmosphereControlLiOH = 2;
        public const int AtmosphereControlRegenerativeScrubber = 3;

        #region Module Fields
        [KSPField]
        public bool lifeSupportEnabled = true;

        [KSPField(isPersistant = true)]
        public int atmosphereControlMode = AtmosphereControlNone;

        [KSPField(isPersistant = true)]
        public float atmosphericControlECRate = 0f;

        [KSPField(isPersistant = true)]
        public float atmosphericControlHeatPerEC = 1f;

        [KSPField(isPersistant = true)]
        public string atmosphereControlSystemName = "";

        [KSPField]
        public float cabinMassFraction = 0.05f;

        [KSPField]
        public float airVolumePerSeat = 2000f;

        [KSPField(isPersistant = true)]
        public float cabinPartConductance = 0.001f;

        [KSPField(isPersistant = true)]
        public float pressureMinimumKPa = 0f;

        [KSPField(isPersistant = true)]
        public bool canUseAmbient = false;

        [KSPField(isPersistant = true)]
        public bool retainsCO2 = true;

        [KSPField(isPersistant = true)]
        public float oxygenWastePerCO2Removed = 0f;

        [KSPField(isPersistant = true)]
        public float pressureExposureTime = 0f;

        [KSPField(isPersistant = true)]
        public float lowO2Time = 0f;

        [KSPField(isPersistant = true)]
        public float lowWaterTime = 0f;

        [KSPField(isPersistant = true)]
        public float lowFoodTime = 0f;

        [KSPField(isPersistant = true)]
        public float lowClimateTime = 0f;

        [KSPField(isPersistant = true)]
        public float tempRangeTime = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Installed System", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string installedAtmosphereControl = "Unpressurized Cabin";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Breathability", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string lsStatus = "Nominal";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Pressure", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string pressureStatus = "";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Master Switch", groupName = "KICKATM", groupDisplayName = "Atmospheric Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool scrubberEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Use Max Capacity", groupName = "KICKATM", groupDisplayName = "Atmospheric Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool useMaxCapacity = false;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Level", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "P1")]
        public float co2Level = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Warning", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string co2WarningReport = "";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "LiOH Use", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "F5", guiUnits = " /s")]
        public float liohUseDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "LiOH Heat", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "F3", guiUnits = " kW")]
        public float liohHeatDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Oxygen Waste", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "F5", guiUnits = " /s")]
        public float oxygenWasteDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Current EC", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "F3", guiUnits = " EC/s")]
        public float atmosphericControlECDisplay = 0f;

        [KSPField]
        public float dbsLifeSupportECRate = 0f;

        [KSPField]
        public float dbsTotalECRate = 0f;
        #endregion

        public double currentHeatFlux = 0;
        public double currentSystemHeatFlux = 0;
        public double currentLiOHReactionHeatFlux = 0;
        public double currentLiOHConsumptionRate = 0;
        public double currentAtmosphericOxygenWasteRate = 0;
        public double currentAtmosphericControlECRate = 0;
        public string rawScrubberStatus = "On";
        internal bool openLoopVentingActive = false;

        bool partActionWindowInitialized = false;
        int lastAtmosphereControlMode = -1;

        public override void OnStart(StartState state)
        {
            PartResourceDefinition wasteDef = PartResourceLibrary.Instance.GetDefinition("Waste");
            if (wasteDef != null) wasteId = wasteDef.id;
            PartResourceDefinition liohDef = PartResourceLibrary.Instance.GetDefinition("LithiumHydroxide");
            if (liohDef != null) liohId = liohDef.id;
            PartResourceDefinition ecDef = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (ecDef != null) ecId = ecDef.id;
            PartResourceDefinition o2Def = PartResourceLibrary.Instance.GetDefinition("Oxygen");
            if (o2Def != null) o2Id = o2Def.id;

            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
            ThermalOnStart(state);
            UpdateDBSTotalECRate();
        }

        public void Start()
        {
            SyncSystemHeatAvailability();
            RefreshScrubberControls();
            RefreshCapabilityControls();
            UpdateDBSLifeSupportECRate();
            UpdateDBSTemperatureControlECRate();
            UpdateDBSTotalECRate();
        }

        public override void OnUpdate()
        {
            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
            ThermalOnUpdate();
            UpdateDBSTotalECRate();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
            ThermalUpdate();
            UpdateDBSTotalECRate();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel.loaded) return;

            LifeSupportStatus data = null;
            if (lifeSupportEnabled && KickLifeSupportScenario.Instance != null)
                data = KickLifeSupportScenario.Instance.GetData(vessel.id);

            double dt = TimeWarp.fixedDeltaTime;
            currentHeatFlux = 0;
            currentSystemHeatFlux = 0;
            currentLiOHReactionHeatFlux = 0;
            currentLiOHConsumptionRate = 0;
            UpdateDBSLifeSupportECRate();

            if (lifeSupportEnabled && data != null)
            {
                int effectiveAtmosphereControlMode = GetEffectiveAtmosphereControlMode();

                // Atmospheric control
                if (effectiveAtmosphereControlMode == AtmosphereControlNone)
                {
                    SetAtmosphericControlStatus(
                        canUseAmbient
                            ? GetAmbientAtmosphereStatus(vessel)
                            : "Inactive");
                }
                else
                {
                    currentSystemHeatFlux += GetAtmosphericControlElectricalHeat();
                    if (effectiveAtmosphereControlMode == AtmosphereControlLiOH &&
                        data.lastLiOHScrubAmount > 0 &&
                        data.activeLiOHScrubberCount > 0 &&
                        dt > 0)
                    {
                        currentLiOHReactionHeatFlux +=
                            (data.lastLiOHScrubAmount / data.activeLiOHScrubberCount / dt) *
                            KickLifeSupportConfig.GetDouble("LIOH_REACTION_HEAT_PER_UNIT", 4.0);
                        currentLiOHConsumptionRate =
                            (data.lastLiOHScrubAmount / data.activeLiOHScrubberCount / dt) *
                            KickLifeSupportScenario.Instance.LithiumHydroxidePerCO2;
                    }

                }
            }

            currentHeatFlux = currentSystemHeatFlux + currentLiOHReactionHeatFlux;
            liohUseDisplay = (float)currentLiOHConsumptionRate;
            liohHeatDisplay = (float)currentLiOHReactionHeatFlux;
            oxygenWasteDisplay = (float)currentAtmosphericOxygenWasteRate;
            atmosphericControlECDisplay =
                (float)Math.Max(currentAtmosphericControlECRate, 0);

            if (lifeSupportEnabled && data != null)
            {
                double displayedCO2 = retainsCO2 ? data.cabinCO2 : cabinCO2;
                double cabinAirVolume = retainsCO2
                    ? GetVesselCabinAirVolume(vessel)
                    : Mathf.Max(part.CrewCapacity, 1) * Mathf.Max(airVolumePerSeat, 0f);
                co2Level = cabinAirVolume > 0
                    ? (float)(displayedCO2 / cabinAirVolume)
                    : 0f;
                if (float.IsNaN(co2Level) || float.IsInfinity(co2Level)) co2Level = 0f;
                RefreshStatusReports(data, co2Level, part.protoModuleCrew.Count);
            }

            ThermalFixedUpdate();
            UpdateDBSTotalECRate();
        }

        void UpdateDBSTotalECRate()
        {
            dbsTotalECRate = dbsLifeSupportECRate + dbsTemperatureControlECRate;
        }

        #region Scrubber Handling

        void RefreshScrubberControls()
        {
            int effectiveAtmosphereControlMode = GetEffectiveAtmosphereControlMode();
            bool hasActiveAtmosphericControlSystem =
                effectiveAtmosphereControlMode != AtmosphereControlNone;
            bool usesLiOH = effectiveAtmosphereControlMode == AtmosphereControlLiOH;
            bool showsOxygenWaste = oxygenWastePerCO2Removed > 0.000001f;
            bool showCabinCO2 = retainsCO2;

            if (!partActionWindowInitialized || lastAtmosphereControlMode != effectiveAtmosphereControlMode)
            {
                lastAtmosphereControlMode = effectiveAtmosphereControlMode;
                partActionWindowInitialized = true;

                if (!usesLiOH)
                {
                    SetLiOHResource(false);
                }
            }

            installedAtmosphereControl = GetAtmosphericControlDisplayName();
            UpdateMasterSwitchLabel();
            UpdateAtmosphericResourceLabels();
            Fields["installedAtmosphereControl"].guiActive = lifeSupportEnabled;
            Fields["lsStatus"].guiActive = lifeSupportEnabled;
            Fields["scrubberEnabled"].guiActive =
                lifeSupportEnabled && hasActiveAtmosphericControlSystem;
            Fields["scrubberEnabled"].guiActiveEditor =
                hasActiveAtmosphericControlSystem;
            Fields["useMaxCapacity"].guiActive =
                lifeSupportEnabled && hasActiveAtmosphericControlSystem && showCabinCO2;
            Fields["useMaxCapacity"].guiActiveEditor =
                hasActiveAtmosphericControlSystem && showCabinCO2;
            Fields["co2Level"].guiActive = lifeSupportEnabled && showCabinCO2;
            Fields["co2WarningReport"].guiActive = false;
            Fields["liohUseDisplay"].guiActive = lifeSupportEnabled && usesLiOH;
            Fields["liohHeatDisplay"].guiActive = lifeSupportEnabled && usesLiOH;
            Fields["oxygenWasteDisplay"].guiActive = lifeSupportEnabled && showsOxygenWaste;
            Fields["atmosphericControlECDisplay"].guiActive =
                lifeSupportEnabled && hasActiveAtmosphericControlSystem;
            Events["ReloadScrubber"].active = usesLiOH;
            Events["ReloadScrubber"].guiActive = lifeSupportEnabled && usesLiOH;
        }

        void UpdateMasterSwitchLabel()
        {
            Fields["scrubberEnabled"].guiName = "CO2 Removal";
        }

        void UpdateAtmosphericResourceLabels()
        {
            bool usesLiOH =
                GetEffectiveAtmosphereControlMode() == AtmosphereControlLiOH;
            bool liOHLacking = usesLiOH && GetLiOHAmount() <= 0.000001;
            bool showsOxygenWaste = oxygenWastePerCO2Removed > 0.000001f;
            bool ecLacking =
                HasActiveAtmosphericControlSystem() &&
                scrubberEnabled &&
                rawScrubberStatus == "No EC";
            bool oxygenUnavailable =
                showsOxygenWaste && rawScrubberStatus == "No O2";
            bool oxygenLimited =
                showsOxygenWaste && rawScrubberStatus == "O2 Limited";

            Fields["liohUseDisplay"].guiName = liOHLacking
                ? KickUIFormat.Bad("LiOH Use (Empty)")
                : "LiOH Use";
            Fields["oxygenWasteDisplay"].guiName = oxygenUnavailable
                ? KickUIFormat.Bad("Oxygen Waste (No Oxygen)")
                : oxygenLimited
                    ? KickUIFormat.Warning("Oxygen Waste (O2 Limited)")
                    : "Oxygen Waste";
            Fields["atmosphericControlECDisplay"].guiName = ecLacking
                ? KickUIFormat.Bad("Current EC (No Electricity)")
                : "Current EC";
            Events["ReloadScrubber"].guiName = liOHLacking
                ? KickUIFormat.Bad("Reload Scrubber (Empty)")
                : "Reload Scrubber";
        }

        double GetLiOHAmount()
        {
            if (liohId == -1 || part == null) return 0;
            PartResource lioh = part.Resources.Get(liohId);
            return lioh != null ? Math.Max(lioh.amount, 0) : 0;
        }

        string GetAtmosphericControlDisplayName()
        {
            if (!string.IsNullOrEmpty(atmosphereControlSystemName))
            {
                return atmosphereControlSystemName;
            }

            switch (atmosphereControlMode)
            {
                case AtmosphereControlOpenLoopVentilation:
                    return "Open-Loop Ventilation";
                case AtmosphereControlLiOH:
                    return "LiOH Scrubber";
                case AtmosphereControlRegenerativeScrubber:
                    return "Regenerative Scrubber";
                default:
                    return canUseAmbient ? "Unpressurized Cabin" : "Pressurized Cabin";
            }
        }

        bool HasActiveAtmosphericControlSystem()
        {
            int effectiveAtmosphereControlMode = GetEffectiveAtmosphereControlMode();
            return effectiveAtmosphereControlMode == AtmosphereControlOpenLoopVentilation ||
                   effectiveAtmosphereControlMode == AtmosphereControlLiOH ||
                   effectiveAtmosphereControlMode == AtmosphereControlRegenerativeScrubber;
        }

        bool IsRegenerativeScrubber()
        {
            return GetEffectiveAtmosphereControlMode() == AtmosphereControlRegenerativeScrubber;
        }

        public static bool UsesOpenLoopVentilation(int mode)
        {
            return mode == AtmosphereControlOpenLoopVentilation;
        }

        public static int GetEffectiveAtmosphereControlMode(int mode, bool retainsCO2)
        {
            return retainsCO2 ? mode : AtmosphereControlNone;
        }

        int GetEffectiveAtmosphereControlMode()
        {
            return GetEffectiveAtmosphereControlMode(atmosphereControlMode, retainsCO2);
        }

        void RefreshStatusReports(LifeSupportStatus data, double currentCO2Level, int crewCount)
        {
            bool hasCrew = crewCount > 0;
            Fields["lsStatus"].guiActive = lifeSupportEnabled && hasCrew;
            Fields["lsStatus"].guiName = "Breathability";
            Fields["co2Level"].guiActive = lifeSupportEnabled && hasCrew && retainsCO2;
            Fields["co2WarningReport"].guiActive = false;
            Fields["pressureStatus"].guiActive = false;
            if (!hasCrew) return;

            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            double breathingRemaining = -1;
            int effectiveAtmosphereControlMode = GetEffectiveAtmosphereControlMode();
            bool ambientOnly =
                effectiveAtmosphereControlMode == AtmosphereControlNone && canUseAmbient;
            bool pressureExposed = pressureMinimumKPa > 0 && pressureExposureTime > 0;

            if (scenario != null)
            {
                if (pressureExposed)
                {
                    breathingRemaining = System.Math.Max(0,
                        scenario.GetPressureExposureGrace(this, vessel) -
                        pressureExposureTime);
                }
                else if (!ambientOnly && lowO2Time > 0)
                {
                    breathingRemaining = scenario.GraceOxygen - lowO2Time;
                }

            }

            lsStatus = BuildBreathabilityStatusReport(
                ambientOnly);

            bool showPressureStatus = pressureMinimumKPa > 0;
            Fields["pressureStatus"].guiActive = lifeSupportEnabled && hasCrew && showPressureStatus;
            Fields["pressureStatus"].guiName = "Pressure";
            if (showPressureStatus)
            {
                pressureStatus = BuildPressureStatusReport(
                    ambientOnly,
                    pressureExposed,
                    breathingRemaining);
            }

            co2WarningReport = FormatCO2Warning(currentCO2Level);
            Fields["co2WarningReport"].guiActive = false;

            if (scenario != null && lowFoodTime > 0)
            {
                nutritionFoodReport = KickUIFormat.Bad($"Food Unavailable ({KickUIFormat.Timer(scenario.GraceFood - lowFoodTime)})");
            }
            else
            {
                nutritionFoodReport = KickUIFormat.Good("Food Available");
            }

            if (scenario != null && lowWaterTime > 0)
            {
                nutritionWaterReport = KickUIFormat.Bad($"Water Unavailable ({KickUIFormat.Timer(scenario.GraceWater - lowWaterTime)})");
            }
            else
            {
                nutritionWaterReport = KickUIFormat.Good("Water Available");
            }
        }

        string BuildBreathabilityStatusReport(
            bool ambientOnly)
        {
            if (!ambientOnly && lowO2Time > 0 && KickLifeSupportScenario.Instance != null)
            {
                return KickUIFormat.Bad(
                    $"Unbreathable Atmosphere ({KickUIFormat.Timer(KickLifeSupportScenario.Instance.GraceOxygen - lowO2Time)})");
            }

            if (ambientOnly && IsVesselUnderwater(vessel))
            {
                return KickUIFormat.Bad("No Breathable Air");
            }

            if (ambientOnly)
            {
                return KickUIFormat.Good("Ambient Atmosphere");
            }

            string breathabilityLabel =
                canUseAmbient && IsAmbientAtmosphereSafe(vessel)
                    ? "Ambient Atmosphere"
                    : "Using Onboard Oxygen";
            return KickUIFormat.Good(breathabilityLabel);
        }

        string BuildPressureStatusReport(
            bool ambientOnly,
            bool pressureExposed,
            double breathingRemaining)
        {
            if (vessel == null)
            {
                return KickUIFormat.Warning("Unknown");
            }

            if (ambientOnly)
            {
                if (pressureMinimumKPa > 0 &&
                    vessel.staticPressurekPa < pressureMinimumKPa &&
                    breathingRemaining >= 0)
                {
                    return KickUIFormat.Bad($"Depressurization ({KickUIFormat.Timer(breathingRemaining)})");
                }

                return KickUIFormat.Good("Pressure Stable");
            }

            if (pressureExposed && breathingRemaining >= 0)
            {
                return KickUIFormat.Bad($"Depressurization ({KickUIFormat.Timer(breathingRemaining)})");
            }

            return KickUIFormat.Good("Pressure Stable");
        }

        public void SetAtmosphericControlStatus(string status)
        {
            rawScrubberStatus = status;
        }

        string FormatCO2Warning(double currentCO2Level)
        {
            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            if (scenario == null) return "";

            if (currentCO2Level >= scenario.CO2FatalLevel)
            {
                return KickUIFormat.Bad($"Critical CO2 ({KickUIFormat.Timer(scenario.GraceOxygen - lowO2Time)})");
            }

            if (currentCO2Level >= scenario.CO2WarningLevel)
            {
                return KickUIFormat.Warning("Elevated CO2");
            }

            return KickUIFormat.Good("Nominal");
        }

        void SetLiOHResource(bool enabled)
        {
            if (liohId == -1) return;
            PartResource lioh = part.Resources.Get(liohId);
            if (lioh == null) return;

            if (enabled)
            {
                PartResource prefab = part.partInfo?.partPrefab?.Resources.Get(liohId);
                double restore = prefab != null ? prefab.maxAmount : 0.5;
                lioh.maxAmount = restore;
                lioh.amount = restore;
            }
            else
            {
                lioh.amount = 0;
                lioh.maxAmount = 0;
            }
        }

        /// <summary>
        /// Allows the user to replace the lithium hydroxide canister
        /// </summary>
        [KSPEvent(guiActive = true, guiName = "Reload Scrubber", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public void ReloadScrubber()
        {
            string cartridgePartName = "KickLSLiOHCartridge";
            double cartridgeVolume = 1.5;

            if (IsRegenerativeScrubber())
            {
                ScreenMessages.PostScreenMessage("Regenerative scrubbers do not use LiOH cartridges.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (GetEffectiveAtmosphereControlMode() != AtmosphereControlLiOH)
            {
                ScreenMessages.PostScreenMessage("This atmospheric control system does not use LiOH cartridges.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            PartResource lioh = part.Resources.Get(liohId);
            PartResource waste = part.Resources.Get(wasteId);
            if (lioh == null)
            {
                ScreenMessages.PostScreenMessage("No LiOH tank found on this part.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (lioh.amount >= lioh.maxAmount * 0.95)
            {
                ScreenMessages.PostScreenMessage("Scrubber is already full.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            double liOHToAdd = lioh.maxAmount - lioh.amount;
            double wasteToStore = cartridgeVolume - liOHToAdd;

            if (waste != null)
            {
                if ((waste.maxAmount - waste.amount) < wasteToStore)
                {
                    ScreenMessages.PostScreenMessage("Cannot reload: Waste storage is full!", 3f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
            }

            if (ConsumePartFromInventory(cartridgePartName))
            {
                lioh.amount = lioh.maxAmount;
                part.RequestResource(wasteId, -wasteToStore);    // Throw away the old cartridge
                ScreenMessages.PostScreenMessage("Scrubber reloaded.", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                ScreenMessages.PostScreenMessage("No LiOH Cartridges found in Inventory.", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        bool ConsumePartFromInventory(string partName)
        {
            foreach (Part p in vessel.parts)
            {
                ModuleInventoryPart inventory = p.FindModuleImplementing<ModuleInventoryPart>();
                if (inventory != null)
                {
                    for (int i = 0; i < inventory.InventorySlots; i++)
                    {
                        if (inventory.storedParts.ContainsKey(i))
                        {
                            StoredPart item = inventory.storedParts[i];

                            if (item.partName == partName)
                            {
                                if (item.quantity > 1)
                                {
                                    item.quantity--;
                                }
                                else
                                {
                                    inventory.storedParts.Remove(i);
                                }

                                MonoUtilities.RefreshPartContextWindow(p);
                                GameEvents.onVesselChange.Fire(this.vessel);
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
        #endregion

        #region Cabin Helpers
        private double GetVesselCabinAirVolume(Vessel v)
        {
            if (KickLifeSupportScenario.Instance != null &&
                KickLifeSupportScenario.Instance.TryGetVesselCabinMetrics(
                    v,
                    out double cachedAirVolume,
                    out _))
            {
                return cachedAirVolume;
            }

            double volume = 0;
            foreach (KickLifeSupportModule module in v.FindPartModulesImplementing<KickLifeSupportModule>())
            {
                if (!module.lifeSupportEnabled) continue;
                if (!module.retainsCO2) continue;
                if (!module.IsPressureSupported()) continue;

                volume += module.part.CrewCapacity * Mathf.Max(module.airVolumePerSeat, 0f);
            }
            return volume;
        }
        #endregion

        #region Dynamic Battery Storage
        void UpdateDBSLifeSupportECRate()
        {
            float estimate = 0f;
            if (lifeSupportEnabled && scrubberEnabled && HasActiveAtmosphericControlSystem())
            {
                int capacity = GetDBSCapacityEstimate();
                float occupancyScale =
                    UsesOpenLoopVentilation(GetEffectiveAtmosphereControlMode())
                        ? GetOpenLoopDBSOccupancyScale()
                        : GetDBSOccupancyScale();
                if (occupancyScale > 0f)
                {
                    estimate += GetScaledECRequestEstimate(
                        Mathf.Max(atmosphericControlECRate, 0f),
                        capacity,
                        occupancyScale);
                }
            }

            dbsLifeSupportECRate = estimate;
        }

        float GetScaledECRequestEstimate(float ecRate, int capacity, float occupancyScale)
        {
            return ecRate * capacity * occupancyScale;
        }

        double GetAtmosphericControlElectricalHeat()
        {
            if (!scrubberEnabled) return 0;
            return currentAtmosphericControlECRate * Mathf.Max(atmosphericControlHeatPerEC, 0f);
        }

        int GetDBSCapacityEstimate()
        {
            if (part == null) return 1;
            return part.CrewCapacity > 0 ? part.CrewCapacity : 1;
        }

        float GetDBSOccupancyScale()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return 1f;

            if (KickLifeSupportScenario.Instance != null &&
                KickLifeSupportScenario.Instance.TryGetVesselCabinMetrics(
                    vessel,
                    out _,
                    out float cachedOccupancyScale))
            {
                return cachedOccupancyScale;
            }

            int totalCrew = 0;
            int totalScrubberCapacity = 0;
            foreach (KickLifeSupportModule module in vessel.FindPartModulesImplementing<KickLifeSupportModule>())
            {
                if (!module.lifeSupportEnabled) continue;
                if (module.retainsCO2 && module.IsPressureSupported())
                {
                    totalCrew += module.part.protoModuleCrew.Count;
                }

                if (!module.retainsCO2 ||
                    !module.scrubberEnabled ||
                    !module.HasActiveAtmosphericControlSystem() ||
                    !module.IsPressureSupported())
                {
                    continue;
                }

                totalScrubberCapacity += module.part.CrewCapacity > 0
                    ? module.part.CrewCapacity
                    : 1;
            }

            if (totalCrew <= 0 || totalScrubberCapacity <= 0) return 0f;
            return Mathf.Min((float)totalCrew / totalScrubberCapacity, 1f);
        }

        float GetOpenLoopDBSOccupancyScale()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return 1f;
            if (!IsPressureSupported()) return 0f;
            if (IsAmbientAtmosphereSafe(vessel)) return 0f;

            int capacity = GetDBSCapacityEstimate();
            int crew = part != null ? part.protoModuleCrew.Count : 0;
            if (capacity <= 0 || crew <= 0) return 0f;
            return Mathf.Min((float)crew / capacity, 1f);
        }

        bool IsPressureSupported()
        {
            if (pressureMinimumKPa <= 0f) return true;
            if (vessel == null || vessel.mainBody == null) return false;
            if (IsVesselUnderwater(vessel)) return false;
            return vessel.staticPressurekPa >= pressureMinimumKPa;
        }
        #endregion

        #region Ambient Atmosphere Helpers
        bool IsAmbientAtmosphereSafe(Vessel v)
        {
            if (v == null || v.mainBody == null) return false;
            if (!v.mainBody.atmosphereContainsOxygen) return false;
            if (IsVesselUnderwater(v)) return false;
            return v.staticPressurekPa >= KickLifeSupportScenario.AmbientPressureMinimumKPa;
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
        #endregion
    }
}

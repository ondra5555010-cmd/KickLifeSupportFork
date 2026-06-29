using System;
using System.Reflection;
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
        public const int AtmosphereControlPressurizedCabin = 1;
        public const int AtmosphereControlOpenLoopVentilation = 2;
        public const int AtmosphereControlLiOH = 3;
        public const int AtmosphereControlRegenerativeScrubber = 4;
        public const int AtmosphereControlSolidAmine = 5;

        #region Module Fields
        [KSPField]
        public bool lifeSupportEnabled = true;

        [KSPField(isPersistant = true)]
        public int atmosphereControlMode = AtmosphereControlNone;

        [KSPField(isPersistant = true)]
        public float atmosphericControlECRate = 0f;

        [KSPField(isPersistant = true)]
        public float atmosphericControlHeatPerEC = 1f;

        [KSPField]
        public float openLoopAtmosphericControlECRate = 0.005f;

        [KSPField]
        public float liohAtmosphericControlECRate = 0.05f;

        [KSPField]
        public float zeoliteAtmosphericControlECRate = 0.2f;

        [KSPField]
        public float solidAmineAtmosphericControlECRate = 0.1f;

        [KSPField]
        public float poweredAtmosphericControlHeatPerEC = 1f;

        [KSPField]
        public float cabinMassFraction = 0.05f;

        [KSPField]
        public float airVolumePerSeat = 2000f;

        [KSPField]
        public float pressurizedCabinPartConductance = 0.001f;

        [KSPField]
        public float openLoopCabinPartConductance = 0.01f;

        [KSPField]
        public float unpressurizedCabinPartConductance = 0.1f;

        [KSPField]
        public float openLoopOxygenMultiplier = 10f;

        [KSPField(isPersistant = true)]
        public float pressureExposureTime = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Installed System", groupName = "KICKATM", groupDisplayName = "AtCon")]
        public string installedAtmosphereControl = "Unpressurized Cabin";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Situation Report", groupName = "KICKATM", groupDisplayName = "AtCon")]
        public string lsStatus = "Nominal";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Master Switch", groupName = "KICKATM", groupDisplayName = "AtCon"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool scrubberEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Level", groupName = "KICKATM", groupDisplayName = "AtCon", guiFormat = "P1")]
        public float co2Level = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Warning", groupName = "KICKATM", groupDisplayName = "AtCon")]
        public string co2WarningReport = "";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "LiOH Use", groupName = "KICKATM", groupDisplayName = "AtCon", guiFormat = "F5", guiUnits = " /s")]
        public float liohUseDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "LiOH Heat", groupName = "KICKATM", groupDisplayName = "AtCon", guiFormat = "F3", guiUnits = " kW")]
        public float liohHeatDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Oxygen Waste", groupName = "KICKATM", groupDisplayName = "AtCon", guiFormat = "F5", guiUnits = " /s")]
        public float oxygenWasteDisplay = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Current EC", groupName = "KICKATM", groupDisplayName = "AtCon", guiFormat = "F3", guiUnits = " EC/s")]
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
        public double currentOpenLoopOxygenWasteRate = 0;
        public double currentAtmosphericControlECRate = 0;
        public string rawScrubberStatus = "On";
        internal bool openLoopVentingActive = false;

        bool partActionWindowInitialized = false;
        int lastAtmosphereControlMode = -1;
        PartModule atmosphericControlSwitch;
        PartModule thermalControlSwitch;
        PropertyInfo b9CurrentSubtypeNameProperty;
        string lastAtmosphericControlSubtype;
        string lastThermalControlSubtype;

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

            SyncB9Selections();
            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
            ThermalOnStart(state);
            UpdateDBSTotalECRate();
        }

        public void Start()
        {
            SyncB9Selections();
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

            SyncB9Selections();
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
            currentOpenLoopOxygenWasteRate = 0;

            UpdateDBSLifeSupportECRate();

            if (lifeSupportEnabled && data != null)
            {
                // Scrubber
                if (atmosphereControlMode == AtmosphereControlNone)
                {
                    SetAtmosphericControlStatus(GetAmbientAtmosphereStatus(vessel));
                }
                else if (IsRegenerativeScrubber())
                {
                    currentSystemHeatFlux += GetAtmosphericControlElectricalHeat();
                }
                else if (atmosphereControlMode == AtmosphereControlLiOH)
                {
                    currentSystemHeatFlux += GetAtmosphericControlElectricalHeat();

                    if (data.lastLiOHScrubAmount > 0 && data.activeLiOHScrubberCount > 0 && dt > 0)
                    {
                        currentLiOHReactionHeatFlux +=
                            (data.lastLiOHScrubAmount / data.activeLiOHScrubberCount / dt) *
                            KickLifeSupportConfig.GetDouble("LIOH_REACTION_HEAT_PER_UNIT", 4.0);
                        currentLiOHConsumptionRate =
                            (data.lastLiOHScrubAmount / data.activeLiOHScrubberCount / dt) *
                            KickLifeSupportScenario.Instance.LithiumHydroxidePerCO2;
                    }
                }
                else if (atmosphereControlMode == AtmosphereControlOpenLoopVentilation)
                {
                    currentSystemHeatFlux += GetAtmosphericControlElectricalHeat();
                    if (rawScrubberStatus == "Active Venting" &&
                        data.lastOpenLoopVentedAmount > 0 &&
                        data.activeOpenLoopVentCapacity > 0 &&
                        dt > 0)
                    {
                        currentOpenLoopOxygenWasteRate =
                            (data.lastOpenLoopVentedAmount *
                                GetDBSCapacityEstimate() /
                                data.activeOpenLoopVentCapacity /
                                dt) *
                            KickLifeSupportScenario.Instance.GetOpenLoopExtraOxygenPerCO2(
                                openLoopOxygenMultiplier);
                    }
                }
            }

            currentHeatFlux = currentSystemHeatFlux + currentLiOHReactionHeatFlux;
            liohUseDisplay = (float)currentLiOHConsumptionRate;
            liohHeatDisplay = (float)currentLiOHReactionHeatFlux;
            oxygenWasteDisplay = (float)currentOpenLoopOxygenWasteRate;
            atmosphericControlECDisplay =
                (float)Math.Max(currentAtmosphericControlECRate, 0);

            if (lifeSupportEnabled && data != null)
            {
                bool isolatedOpenLoopCabin =
                    UsesOpenLoopVentilation(atmosphereControlMode) &&
                    !IsOpenLoopPressureUsable();
                double displayedCO2 = isolatedOpenLoopCabin ? cabinCO2 : data.cabinCO2;
                double cabinAirVolume = isolatedOpenLoopCabin
                    ? Mathf.Max(part.CrewCapacity, 1) * Mathf.Max(airVolumePerSeat, 0f)
                    : GetVesselCabinAirVolume(vessel);
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

        void SyncB9Selections()
        {
            string atmosphereSubtype = GetB9SubtypeName(
                ref atmosphericControlSwitch,
                "kickLSScrubberSwitch");
            if (atmosphereSubtype != null &&
                atmosphereSubtype != lastAtmosphericControlSubtype)
            {
                lastAtmosphericControlSubtype = atmosphereSubtype;
                ApplyAtmosphericControlSubtype(atmosphereSubtype);
            }

            string thermalSubtype = GetB9SubtypeName(
                ref thermalControlSwitch,
                "kickThermalControlSwitch");
            if (thermalSubtype != null &&
                thermalSubtype != lastThermalControlSubtype)
            {
                lastThermalControlSubtype = thermalSubtype;
                ApplyThermalControlSubtype(thermalSubtype);
            }
        }

        string GetB9SubtypeName(ref PartModule cachedSwitch, string switchModuleID)
        {
            if (part == null) return null;

            if (cachedSwitch == null || cachedSwitch.part != part)
            {
                foreach (PartModule module in part.Modules)
                {
                    if (module.moduleName == "ModuleB9PartSwitch" &&
                        GetB9ModuleID(module) == switchModuleID)
                    {
                        cachedSwitch = module;
                        break;
                    }
                }
            }

            if (cachedSwitch == null) return null;

            if (b9CurrentSubtypeNameProperty == null)
            {
                b9CurrentSubtypeNameProperty = cachedSwitch.GetType().GetProperty(
                    "CurrentSubtypeName",
                    BindingFlags.Instance | BindingFlags.Public);
            }

            return b9CurrentSubtypeNameProperty?.GetValue(cachedSwitch, null) as string;
        }

        string GetB9ModuleID(PartModule module)
        {
            FieldInfo moduleIDField = module?.GetType().GetField(
                "moduleID",
                BindingFlags.Instance | BindingFlags.Public);
            return moduleIDField?.GetValue(module) as string;
        }

        void ApplyAtmosphericControlSubtype(string subtype)
        {
            switch (subtype)
            {
                case "UnpressurizedCabin":
                    SetAtmosphericControlConfiguration(AtmosphereControlNone, 0, 0);
                    break;
                case "OpenLoopVenting":
                    SetAtmosphericControlConfiguration(
                        AtmosphereControlOpenLoopVentilation,
                        openLoopAtmosphericControlECRate,
                        poweredAtmosphericControlHeatPerEC);
                    break;
                case "PressurizedCabin":
                    SetAtmosphericControlConfiguration(AtmosphereControlPressurizedCabin, 0, 0);
                    break;
                case "LiOH":
                    SetAtmosphericControlConfiguration(
                        AtmosphereControlLiOH,
                        liohAtmosphericControlECRate,
                        poweredAtmosphericControlHeatPerEC);
                    break;
                case "Zeolite":
                    SetAtmosphericControlConfiguration(
                        AtmosphereControlRegenerativeScrubber,
                        zeoliteAtmosphericControlECRate,
                        poweredAtmosphericControlHeatPerEC);
                    break;
                case "SolidAmine":
                    SetAtmosphericControlConfiguration(
                        AtmosphereControlSolidAmine,
                        solidAmineAtmosphericControlECRate,
                        poweredAtmosphericControlHeatPerEC);
                    break;
            }
        }

        void SetAtmosphericControlConfiguration(int mode, float ecRate, float heatPerEC)
        {
            atmosphereControlMode = mode;
            atmosphericControlECRate = ecRate;
            atmosphericControlHeatPerEC = heatPerEC;
        }

        void ApplyThermalControlSubtype(string subtype)
        {
            heatPumpAvailable = subtype == "HeatPump";
            waterEvaporatorAvailable = subtype == "WaterEvaporator";
            airCoolingAvailable = subtype == "AirCooling";
        }

        #region Scrubber Handling

        void RefreshScrubberControls()
        {
            if (!partActionWindowInitialized || lastAtmosphereControlMode != atmosphereControlMode)
            {
                lastAtmosphereControlMode = atmosphereControlMode;
                partActionWindowInitialized = true;

                bool usesLiOH = atmosphereControlMode == AtmosphereControlLiOH;
                bool hasScrubber = lifeSupportEnabled && HasActiveAtmosphericControlSystem();
                bool hasCabinAtmosphere = lifeSupportEnabled && atmosphereControlMode != AtmosphereControlNone;

                Fields["installedAtmosphereControl"].guiActive = lifeSupportEnabled;
                Fields["lsStatus"].guiActive = lifeSupportEnabled;
                Fields["co2Level"].guiActive = hasCabinAtmosphere;
                Fields["co2WarningReport"].guiActive = hasCabinAtmosphere;
                Fields["liohUseDisplay"].guiActive = usesLiOH;
                Fields["liohHeatDisplay"].guiActive = usesLiOH;
                Fields["oxygenWasteDisplay"].guiActive =
                    atmosphereControlMode == AtmosphereControlOpenLoopVentilation;
                Fields["atmosphericControlECDisplay"].guiActive = hasScrubber;
                Fields["scrubberEnabled"].guiActive = hasScrubber;
                Events["ReloadScrubber"].active = lifeSupportEnabled && usesLiOH;
                Events["ReloadScrubber"].guiActive = lifeSupportEnabled && usesLiOH;

                if (!usesLiOH)
                {
                    SetLiOHResource(false);
                }
            }

            installedAtmosphereControl = GetAtmosphericControlDisplayName();
            UpdateMasterSwitchLabel();
            UpdateAtmosphericResourceLabels();
        }

        void UpdateMasterSwitchLabel()
        {
            Fields["scrubberEnabled"].guiName = "CO2 Removal";
        }

        void UpdateAtmosphericResourceLabels()
        {
            bool usesLiOH =
                atmosphereControlMode == AtmosphereControlLiOH;
            bool liOHLacking = usesLiOH && GetLiOHAmount() <= 0.000001;
            bool ecLacking =
                HasActiveAtmosphericControlSystem() &&
                scrubberEnabled &&
                rawScrubberStatus == "No EC";

            Fields["liohUseDisplay"].guiName = liOHLacking
                ? KickUIFormat.Bad("LiOH Use (Empty)")
                : "LiOH Use";
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
            switch (atmosphereControlMode)
            {
                case AtmosphereControlOpenLoopVentilation:
                    return "Open-Loop Ventilation";
                case AtmosphereControlLiOH:
                    return "LiOH Scrubber";
                case AtmosphereControlRegenerativeScrubber:
                    return "Zeolite Molecular Sieve";
                case AtmosphereControlSolidAmine:
                    return "Solid Amine Swingbed";
                case AtmosphereControlPressurizedCabin:
                    return "Pressurized Cabin";
                default:
                    return "Unpressurized Cabin";
            }
        }

        bool HasActiveAtmosphericControlSystem()
        {
            return atmosphereControlMode == AtmosphereControlOpenLoopVentilation ||
                   atmosphereControlMode == AtmosphereControlLiOH ||
                   IsRegenerativeScrubber();
        }

        bool IsRegenerativeScrubber()
        {
            return atmosphereControlMode == AtmosphereControlRegenerativeScrubber ||
                   atmosphereControlMode == AtmosphereControlSolidAmine;
        }

        public static bool UsesOpenLoopVentilation(int mode)
        {
            return mode == AtmosphereControlOpenLoopVentilation;
        }

        void RefreshStatusReports(LifeSupportStatus data, double currentCO2Level, int crewCount)
        {
            bool hasCrew = crewCount > 0;
            Fields["lsStatus"].guiActive = lifeSupportEnabled && hasCrew;
            Fields["co2Level"].guiActive = lifeSupportEnabled && atmosphereControlMode != AtmosphereControlNone;
            Fields["co2WarningReport"].guiActive = false;
            if (!hasCrew) return;

            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            double breathingRemaining = -1;
            bool ambientOnly = atmosphereControlMode == AtmosphereControlNone;
            bool pressureExposed =
                (ambientOnly || UsesOpenLoopVentilation(atmosphereControlMode)) &&
                pressureExposureTime > 0;

            if (scenario != null)
            {
                if (pressureExposed)
                {
                    breathingRemaining = System.Math.Max(0,
                        scenario.GetPressureExposureGrace(atmosphereControlMode, vessel) -
                        pressureExposureTime);
                }
                else if (!ambientOnly && data.lowO2Time > 0)
                {
                    breathingRemaining = scenario.GraceOxygen - data.lowO2Time;
                }

            }

            if (breathingRemaining >= 0)
            {
                string label = pressureExposed
                    ? (ambientOnly
                        ? vessel != null &&
                          vessel.staticPressurekPa < KickLifeSupportScenario.UnpressurizedPressureFailureKPa
                            ? "Pressure Too Low"
                            : "No Ambient Air"
                        : "Cabin Depressurized")
                    : "Unbreathable Atmosphere";
                lsStatus = KickUIFormat.ReportLine(KickUIFormat.Bad($"{label} ({KickUIFormat.Timer(breathingRemaining)})"));
            }
            else
            {
                string atmosphereLabel = ambientOnly
                    ? "Ambient Air Safe"
                    : UsesOpenLoopVentilation(atmosphereControlMode)
                        ? IsAmbientAtmosphereSafe(vessel)
                            ? "Ambient Atmosphere"
                            : "Open-Loop Pressure Stable"
                        : "Breathable Atmosphere";
                lsStatus = KickUIFormat.ReportLine(KickUIFormat.Good(atmosphereLabel));
            }

            co2WarningReport = FormatCO2Warning(currentCO2Level, data);
            Fields["co2WarningReport"].guiActive =
                lifeSupportEnabled &&
                atmosphereControlMode != AtmosphereControlNone &&
                KickLifeSupportScenario.Instance != null &&
                currentCO2Level >= KickLifeSupportScenario.Instance.CO2WarningLevel;

            if (scenario != null && data.lowFoodTime > 0)
            {
                nutritionFoodReport = KickUIFormat.Bad($"Food Unavailable ({KickUIFormat.Timer(scenario.GraceFood - data.lowFoodTime)})");
            }
            else
            {
                nutritionFoodReport = KickUIFormat.Good("Food Available");
            }

            if (scenario != null && data.lowWaterTime > 0)
            {
                nutritionWaterReport = KickUIFormat.Bad($"Water Unavailable ({KickUIFormat.Timer(scenario.GraceWater - data.lowWaterTime)})");
            }
            else
            {
                nutritionWaterReport = KickUIFormat.Good("Water Available");
            }
        }

        public void SetAtmosphericControlStatus(string status)
        {
            rawScrubberStatus = status;
        }

        string FormatCO2Warning(double currentCO2Level, LifeSupportStatus data)
        {
            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            if (scenario == null) return "";

            if (currentCO2Level >= scenario.CO2FatalLevel)
            {
                return KickUIFormat.Bad($"Critical CO2 ({KickUIFormat.Timer(scenario.GraceOxygen - data.lowO2Time)})");
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
        [KSPEvent(guiActive = true, guiName = "Reload Scrubber", groupName = "KICKATM", groupDisplayName = "AtCon")]
        public void ReloadScrubber()
        {
            string cartridgePartName = "KickLSLiOHCartridge";
            double cartridgeVolume = 1.5;

            if (IsRegenerativeScrubber())
            {
                ScreenMessages.PostScreenMessage("Regenerative scrubbers do not use LiOH cartridges.", 3f, ScreenMessageStyle.UPPER_CENTER);
                return;
            }

            if (atmosphereControlMode != AtmosphereControlLiOH)
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
                if (module.atmosphereControlMode == AtmosphereControlNone) continue;
                if (UsesOpenLoopVentilation(module.atmosphereControlMode) &&
                    !module.IsOpenLoopPressureUsable()) continue;

                volume += module.part.CrewCapacity * Mathf.Max(module.airVolumePerSeat, 0f);
            }
            return volume;
        }
        #endregion

        #region Dynamic Battery Storage
        void UpdateDBSLifeSupportECRate()
        {
            float estimate = 0f;
            int capacity = GetDBSCapacityEstimate();
            float occupancyScale = GetDBSOccupancyScale();

            if (lifeSupportEnabled && scrubberEnabled && HasActiveAtmosphericControlSystem())
            {
                estimate += GetScaledECRequestEstimate(
                    Mathf.Max(atmosphericControlECRate, 0f),
                    capacity,
                    occupancyScale);
            }

            dbsLifeSupportECRate = estimate;
        }

        float GetScaledECRequestEstimate(float ecRate, int capacity, float occupancyScale)
        {
            return ecRate * capacity * occupancyScale;
        }

        double GetAtmosphericControlElectricalHeat()
        {
            if (!scrubberEnabled || GetDBSOccupancyScale() <= 0f) return 0;
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
                if (module.atmosphereControlMode != AtmosphereControlNone)
                {
                    bool openLoopCabinExposed =
                        UsesOpenLoopVentilation(module.atmosphereControlMode) &&
                        !module.IsOpenLoopPressureUsable();
                    if (!openLoopCabinExposed)
                    {
                        totalCrew += module.part.protoModuleCrew.Count;
                    }
                }

                if (!module.scrubberEnabled || !module.HasActiveAtmosphericControlSystem()) continue;
                if (module.atmosphereControlMode == AtmosphereControlOpenLoopVentilation &&
                    !module.IsOpenLoopPressureUsable()) continue;

                totalScrubberCapacity += module.part.CrewCapacity > 0
                    ? module.part.CrewCapacity
                    : 1;
            }

            if (totalCrew <= 0 || totalScrubberCapacity <= 0) return 0f;
            return Mathf.Min((float)totalCrew / totalScrubberCapacity, 1f);
        }

        bool IsOpenLoopPressureUsable()
        {
            if (vessel == null || vessel.mainBody == null) return false;
            if (IsVesselUnderwater(vessel)) return false;
            return vessel.staticPressurekPa >= KickLifeSupportScenario.OpenLoopPressureMinimumKPa;
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

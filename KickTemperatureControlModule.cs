using System;
using SystemHeat;
using UnityEngine;

namespace KickLifeSupport
{
    public class KickTemperatureControlModule : PartModule, IAnalyticTemperatureModifier
    {
        KickLifeSupportSettings gameSettings;

        #region Thermal GUI Fields
        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Situation Report", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        public string climateControlStatus = "On";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Master Switch", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool climateControlEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Thermostat", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        [UI_FloatRange(minValue = 10f, maxValue = 30f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float thermostatTemp = 22f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Cabin Temperature", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F1", guiUnits = " C")]
        public float cabinTemp = 0f;

        [KSPField(isPersistant = true)]
        public bool thermalStateInitialized = false;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Part Temperature", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F1", guiUnits = " C")]
        public float partTempDisplay = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Total Flux", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float totalFlux = 0f;

        [KSPField] public float heatFlux = 0;
        [KSPField] public float passiveFlux = 0;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Heater", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool heaterEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Heater Limit", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F2", guiUnits = " kW")]
        [UI_FloatRange(minValue = 0f, maxValue = 40f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float heaterHeat = 0.5f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Heat Created", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float heaterOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Evaporator", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool evaporatorEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Evaporator Feed", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        [UI_ChooseOption(scene = UI_Scene.All, options = new[] { "Waste First", "Waste Only", "Fresh First", "Fresh Only" })]
        public string evaporatorFeedMode = "Waste First";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Evaporator Limit", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F2", guiUnits = " kW")]
        [UI_FloatRange(minValue = 0f, maxValue = 40f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float waterEvaporatorHeat = 1f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Cooling", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float evaporatorOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Air Cooling", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool airCoolingEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Air Cooling Limit", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F2", guiUnits = " kW")]
        [UI_FloatRange(minValue = 0f, maxValue = 10f, stepIncrement = 0.05f, scene = UI_Scene.All)]
        public float airCoolingHeat = 0.25f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Cooling", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float airCoolingOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Heat Pump", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool heatPumpEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Heat Pump Limit", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F2", guiUnits = " kW")]
        [UI_FloatRange(minValue = 0f, maxValue = 40f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float heatPumpHeat = 0.5f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Cooling", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float heatPumpOutput = 0f;

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Food", groupName = "KICKNUTRITION", groupDisplayName = "Nutrition")]
        public string nutritionFoodReport = "Available";

        [KSPField(guiActive = false, guiActiveEditor = false, guiName = "Water", groupName = "KICKNUTRITION", groupDisplayName = "Nutrition")]
        public string nutritionWaterReport = "Available";

        [KSPField] public float cabinPartCouplingDisplay = 0f;
        [KSPField] public string heaterStatus = "Off";
        [KSPField] public string heatPumpStatus = "Off";
        [KSPField] public string waterEvaporatorStatus = "Off";
        [KSPField] public string airCoolingStatus = "Off";
        [KSPField] public float waterEvaporatorWaterRate = 0f;
        [KSPField(isPersistant = true)]
        public bool debugThermalLogging = true;

        [KSPField]
        public float dbsTemperatureControlECRate = 0f;
        #endregion

        #region Thermal Rates
        [KSPField] public float systemECRate = 0.03f;
        [KSPField] public float systemHeat = 0.03f;
        [KSPField] public float heatPumpECRate = 0.1f;
        [KSPField] public float waterEvaporatorECRate = 0.01f;
        [KSPField] public float airCoolingECRate = 0.05f;
        [KSPField] public float heatPumpLoopNominalTemp = 320f;
        [KSPField] public float heatPumpLoopMaxTemp = 370f;
        [KSPField] public string heatPumpSystemHeatModuleID = "kickECS";
        [KSPField] public bool heatPumpAvailable = true;
        [KSPField] public bool waterEvaporatorAvailable = false;
        [KSPField] public bool airCoolingAvailable = false;
        [KSPField(guiName = "Coolant Loop Load", guiFormat = "F3", guiUnits = " kW")]
        public float systemHeatFluxEstimate = 0f;
        #endregion

        #region Thermal Constants
        internal const double thermostatDeadband = 2;
        internal const double airSpecificHeat = 1000.0;
        internal const double genericCabinSpecificHeat = 1000.0;
        internal const double airDensity = 0.001225;
        internal const double insulatedCabinPartConductanceKWPerK = 0.001;
        internal const double partiallyPressurizedCabinPartConductanceKWPerK = 0.01;
        internal const double unpressurizedCabinPartConductanceKWPerK = 0.1;
        internal const double effectiveCabinPartMassFraction = 0.05;
        internal const double waterEvaporatorTransferEfficiency = 0.9;
        internal const double wasteWaterCoolingFactor = 0.8;
        internal const double wasteWaterResidueMassFraction = 0.2;
        internal const double waterEvaporatorEnergyKJPerUnit = 2300.0;
        internal const double waterEvaporatorFullPressureKPa = 0.5;
        internal const double waterEvaporatorMaxPressureKPa = 2.5;
        internal const double airCoolingMinPressureKPa = 1.0;
        internal const double airCoolingFullPressureKPa = 20.0;
        #endregion

        public bool isHeaterActive = false;
        public bool isHeatPumpActive = false;
        public bool isWaterEvaporatorActive = false;
        public bool isAirCoolingActive = false;
        int ecId = -1;
        int waterId = -1;
        int wasteWaterId = -1;
        int wasteId = -1;
        double wasteWaterResidueUnitsPerUnit = 0.2;
        double cachedAnalyticTemp;
        double currentTemperatureControlECRate;
        double currentHeaterECRate;
        double currentEvaporatorECRate;
        double currentAirCoolingECRate;
        double currentHeatPumpECRate;
        double lastThermalLogTime = -999;
        double lastAnalyticLogTime = -999;
        float lastEditorSystemHeatFlux = float.NaN;
        float lastEditorSystemHeatSourceTemp = float.NaN;
        bool lastEditorSystemHeatUseForNominal;
        ModuleSystemHeat cachedSystemHeatModule;
        double debugAvionicsHeat;
        double debugLifeSupportHeat;
        double debugCrewHeat;
        double debugSystemHeat;
        double debugHeaterHeat;
        double debugHeatPumpHeat;
        double debugHeatPumpWasteHeat;
        double debugHeatPumpLoopTemp;
        double debugWaterEvaporatorHeat;
        double debugWaterEvaporatorRate;
        double debugWaterAvailable;
        double debugWasteWaterAvailable;
        double debugWasteWaterRate;
        double debugWasteProducedRate;
        double debugWaterEvaporatorPressureFactor;
        double debugAirCoolingHeat;
        double debugAirCoolingPressureFactor;
        double debugPartHeat;
        double debugPassiveChangeK;
        double debugActiveChangeK;
        double debugCabinHeatCapacity;
        double debugCabinToPartKW;
        double debugCabinPartConductance;
        double debugStockPartFluxKW;
        bool evaporatorAtSetLimit;
        bool evaporatorEnvironmentLimited;
        bool evaporatorResourceLimited;
        bool airCoolingAtSetLimit;
        bool airCoolingEnvironmentLimited;
        bool heatPumpAtSetLimit;
        bool heatPumpEnvironmentLimited;

        public override void OnStart(StartState state)
        {
            gameSettings = HighLogic.CurrentGame.Parameters.CustomParams<KickLifeSupportSettings>();
            NormalizeEvaporatorFeedMode();

            PartResourceDefinition ecDef = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (ecDef != null) ecId = ecDef.id;
            PartResourceDefinition waterDef = PartResourceLibrary.Instance.GetDefinition("Water");
            if (waterDef != null) waterId = waterDef.id;
            PartResourceDefinition wasteWaterDef = PartResourceLibrary.Instance.GetDefinition("WasteWater");
            if (wasteWaterDef != null) wasteWaterId = wasteWaterDef.id;
            PartResourceDefinition wasteDef = PartResourceLibrary.Instance.GetDefinition("Waste");
            if (wasteDef != null) wasteId = wasteDef.id;
            if (wasteWaterDef != null && wasteDef != null && wasteDef.density > 0)
            {
                wasteWaterResidueUnitsPerUnit =
                    wasteWaterDef.density * wasteWaterResidueMassFraction / wasteDef.density;
            }

            InitializeThermalStateIfNeeded();
            ResolveSystemHeatModule();
            SyncSystemHeatAvailability();

            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        public override void OnUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor) return;

            SyncSystemHeatAvailability();
            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            SyncSystemHeatAvailability();
            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
            UpdateEditorSystemHeatEstimate();
            UpdateEditorWaterEvaporatorEstimate();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel.loaded) return;

            InitializeThermalStateIfNeeded();
            SyncSystemHeatAvailability();

            double cabinHeatFlux = 0;
            double partHeatFlux = 0;
            debugAvionicsHeat = 0;
            debugLifeSupportHeat = 0;
            debugCrewHeat = 0;
            debugSystemHeat = 0;
            debugHeaterHeat = 0;
            debugHeatPumpHeat = 0;
            debugHeatPumpWasteHeat = 0;
            debugHeatPumpLoopTemp = 0;
            debugWaterEvaporatorHeat = 0;
            debugWaterEvaporatorRate = 0;
            debugWaterAvailable = 0;
            debugWasteWaterAvailable = 0;
            debugWasteWaterRate = 0;
            debugWasteProducedRate = 0;
            debugWaterEvaporatorPressureFactor = 0;
            debugAirCoolingHeat = 0;
            debugAirCoolingPressureFactor = 0;
            debugPartHeat = 0;
            debugPassiveChangeK = 0;
            debugActiveChangeK = 0;
            debugCabinHeatCapacity = 0;
            debugCabinToPartKW = 0;
            debugCabinPartConductance = 0;
            debugStockPartFluxKW = 0;
            heaterOutput = 0;
            evaporatorOutput = 0;
            airCoolingOutput = 0;
            heatPumpOutput = 0;
            evaporatorAtSetLimit = false;
            evaporatorEnvironmentLimited = false;
            evaporatorResourceLimited = false;
            airCoolingAtSetLimit = false;
            airCoolingEnvironmentLimited = false;
            heatPumpAtSetLimit = false;
            heatPumpEnvironmentLimited = false;

            KickAvionicsModule avionics = part.FindModuleImplementing<KickAvionicsModule>();
            if (avionics != null)
            {
                debugAvionicsHeat = avionics.currentHeatFlux;
                cabinHeatFlux += debugAvionicsHeat;
            }

            KickLifeSupportModule lifeSupport = part.FindModuleImplementing<KickLifeSupportModule>();
            if (lifeSupport != null)
            {
                debugLifeSupportHeat = lifeSupport.currentHeatFlux;
                cabinHeatFlux += lifeSupport.currentSystemHeatFlux;
                cabinHeatFlux += lifeSupport.currentLiOHReactionHeatFlux;
                nutritionFoodReport = lifeSupport.foodReport;
                nutritionWaterReport = lifeSupport.waterReport;
            }

            int crewCount = part.protoModuleCrew.Count;
            if (crewCount > 0 && KickLifeSupportScenario.Instance != null)
            {
                debugCrewHeat = crewCount * KickLifeSupportScenario.Instance.kerbalHeat;
                cabinHeatFlux += debugCrewHeat;
            }

            if (gameSettings != null && gameSettings.useCabinTempSystem)
            {
                RunThermalLogic(ref cabinHeatFlux, ref partHeatFlux, lifeSupport);

                debugStockPartFluxKW = partHeatFlux;
                part.AddThermalFlux(debugStockPartFluxKW);
            }
            else
            {
                cabinHeatFlux = 0;
                partHeatFlux = 0;
            }

            debugPartHeat = partHeatFlux;
            heatFlux = (float)cabinHeatFlux;
            partTempDisplay = (float)KToC(part.temperature);
            cabinPartCouplingDisplay = (float)debugCabinPartConductance;
            totalFlux = heatFlux + passiveFlux;
            RefreshTemperatureReport();
            UpdateDBSTemperatureControlECRate();
            LogThermalDebug();
        }

        void RefreshCapabilityControls()
        {
            Fields["climateControlEnabled"].guiName = $"Master Switch ({FormatECRate(currentTemperatureControlECRate)})";
            Fields["heaterEnabled"].guiName = $"Heater ({FormatECRate(currentHeaterECRate)})";
            Fields["evaporatorEnabled"].guiName = $"Evaporator ({FormatECRate(currentEvaporatorECRate)})";
            Fields["airCoolingEnabled"].guiName = $"Air Cooling ({FormatECRate(currentAirCoolingECRate)})";
            Fields["heatPumpEnabled"].guiName = $"Heat Pump ({FormatECRate(currentHeatPumpECRate)})";
            Fields["cabinTemp"].guiName =
                cabinTemp < 5f || cabinTemp > 45f
                    ? KickUIFormat.Bad("Cabin Temperature")
                    : cabinTemp < 10f || cabinTemp > 40f
                        ? KickUIFormat.Warning("Cabin Temperature")
                        : "Cabin Temperature";
            Fields["evaporatorOutput"].guiName = GetEvaporatorOutputLabel();
            Fields["airCoolingOutput"].guiName = GetAirCoolingOutputLabel();
            Fields["heatPumpOutput"].guiName = GetHeatPumpOutputLabel();
            Fields["climateControlEnabled"].guiActive = true;
            Fields["climateControlEnabled"].guiActiveEditor = true;
            Fields["climateControlStatus"].guiActive = true;
            Fields["thermostatTemp"].guiActive = true;
            Fields["thermostatTemp"].guiActiveEditor = true;
            Fields["cabinTemp"].guiActive = true;
            Fields["partTempDisplay"].guiActive = true;
            Fields["totalFlux"].guiActive = true;
            Fields["heaterEnabled"].guiActive = true;
            Fields["heaterEnabled"].guiActiveEditor = true;
            Fields["heaterHeat"].guiActive = true;
            Fields["heaterHeat"].guiActiveEditor = true;
            Fields["heaterOutput"].guiActive = heaterOutput > 0.0005f;
            Fields["evaporatorEnabled"].guiActive = waterEvaporatorAvailable;
            Fields["evaporatorEnabled"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["evaporatorFeedMode"].guiActive = waterEvaporatorAvailable;
            Fields["evaporatorFeedMode"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["heatPumpHeat"].guiActive = heatPumpAvailable;
            Fields["heatPumpHeat"].guiActiveEditor = heatPumpAvailable;
            Fields["heatPumpEnabled"].guiActive = heatPumpAvailable;
            Fields["heatPumpEnabled"].guiActiveEditor = heatPumpAvailable;
            Fields["heatPumpOutput"].guiActive = heatPumpAvailable;
            Fields["waterEvaporatorHeat"].guiActive = waterEvaporatorAvailable;
            Fields["waterEvaporatorHeat"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["evaporatorOutput"].guiActive = waterEvaporatorAvailable;
            Fields["airCoolingEnabled"].guiActive = airCoolingAvailable;
            Fields["airCoolingEnabled"].guiActiveEditor = airCoolingAvailable;
            Fields["airCoolingHeat"].guiActive = airCoolingAvailable;
            Fields["airCoolingHeat"].guiActiveEditor = airCoolingAvailable;
            Fields["airCoolingOutput"].guiActive = airCoolingAvailable;

            KickLifeSupportModule lifeSupport = part?.FindModuleImplementing<KickLifeSupportModule>();
            bool hasCrew = part != null && part.protoModuleCrew.Count > 0;
            Fields["nutritionFoodReport"].guiActive =
                lifeSupport != null && lifeSupport.lifeSupportEnabled && hasCrew;
            Fields["nutritionWaterReport"].guiActive =
                lifeSupport != null && lifeSupport.lifeSupportEnabled && hasCrew;
        }

        string FormatECRate(double rate)
        {
            return rate < 0.0005 ? "0 EC/s" : $"{rate:F3} EC/s";
        }

        void NormalizeEvaporatorFeedMode()
        {
            if (evaporatorFeedMode == "Waste Water Priority") evaporatorFeedMode = "Waste First";
            else if (evaporatorFeedMode == "Waste Water Only") evaporatorFeedMode = "Waste Only";
            else if (evaporatorFeedMode == "Fresh Water Priority") evaporatorFeedMode = "Fresh First";
            else if (evaporatorFeedMode == "Fresh Water Only") evaporatorFeedMode = "Fresh Only";
        }

        string GetEvaporatorOutputLabel()
        {
            if (waterEvaporatorStatus == "Atmosphere") return KickUIFormat.Bad("Cooling (Blocked by Atmosphere)");
            if (waterEvaporatorStatus == "No Water") return KickUIFormat.Bad("Cooling (No Water)");
            if (waterEvaporatorStatus == "No Power") return KickUIFormat.Bad("Cooling (No Power)");
            if (evaporatorResourceLimited) return KickUIFormat.Warning("Cooling (Water Limited)");
            if (evaporatorEnvironmentLimited) return KickUIFormat.Warning("Cooling (Atmosphere Limited)");
            if (evaporatorAtSetLimit) return KickUIFormat.Warning("Cooling (At Limit)");
            return "Cooling";
        }

        string GetAirCoolingOutputLabel()
        {
            if (airCoolingStatus == "No Atmosphere") return KickUIFormat.Bad("Cooling (No Atmosphere)");
            if (airCoolingStatus == "No Power") return KickUIFormat.Bad("Cooling (No Power)");
            if (airCoolingEnvironmentLimited) return KickUIFormat.Warning("Cooling (Thin Atmosphere)");
            if (airCoolingAtSetLimit) return KickUIFormat.Warning("Cooling (At Limit)");
            return "Cooling";
        }

        string GetHeatPumpOutputLabel()
        {
            if (heatPumpStatus == "No SystemHeat") return KickUIFormat.Bad("Cooling (System Unavailable)");
            if (heatPumpStatus == "No Loop") return KickUIFormat.Bad("Cooling (No Loop)");
            if (heatPumpStatus == "No Radiator") return KickUIFormat.Bad("Cooling (No Radiator)");
            if (heatPumpStatus == "Loop Hot") return KickUIFormat.Bad("Cooling (Loop Too Hot)");
            if (heatPumpStatus == "No Power") return KickUIFormat.Bad("Cooling (No Power)");
            if (heatPumpEnvironmentLimited) return KickUIFormat.Warning("Cooling (Loop Limited)");
            if (heatPumpAtSetLimit) return KickUIFormat.Warning("Cooling (At Limit)");
            return "Cooling";
        }

        void UpdateDBSTemperatureControlECRate()
        {
            float estimate = 0f;
            if (climateControlEnabled && (gameSettings == null || gameSettings.useCabinTempSystem))
            {
                if (HighLogic.LoadedSceneIsEditor)
                {
                    estimate = GetEditorTemperatureControlECRate();
                }
                else
                {
                    estimate = (float)currentTemperatureControlECRate;
                }
            }

            dbsTemperatureControlECRate = estimate;
        }

        float GetEditorTemperatureControlECRate()
        {
            return Math.Max(systemECRate, 0) +
                (heaterEnabled ? Math.Max(heaterHeat, 0) : 0) +
                GetWaterEvaporatorEditorECRate() +
                GetAirCoolingEditorECRate() +
                GetHeatPumpEditorECRate();
        }

        float GetWaterEvaporatorEditorECRate()
        {
            if (!waterEvaporatorAvailable || !evaporatorEnabled || waterEvaporatorHeat <= 0) return 0;

            return Math.Max(waterEvaporatorECRate, 0);
        }

        float GetAirCoolingEditorECRate()
        {
            if (!airCoolingAvailable || !airCoolingEnabled || airCoolingHeat <= 0) return 0;

            return Math.Max(airCoolingECRate, 0);
        }

        float GetHeatPumpEditorECRate()
        {
            if (!heatPumpAvailable || !heatPumpEnabled) return 0;

            double heatPumpLimit = Math.Max(heatPumpHeat, 0);
            if (heatPumpLimit <= 0 || heatPumpECRate <= 0) return 0;

            ModuleSystemHeat heatModule = GetSystemHeatModule();
            if (!IsSystemHeatLoopAvailable(heatModule)) return 0;

            double loopTemperature = GetSystemHeatLoopTemperature(heatModule);
            double loopFactor = loopTemperature > 0 ? GetHeatPumpLoopFactor(loopTemperature) : 1;
            return (float)(Math.Max(heatPumpECRate, 0) * loopFactor);
        }

        float GetEditorSystemHeatFluxEstimate()
        {
            if (!heatPumpAvailable || !heatPumpEnabled) return 0;

            double heatPumpLimit = Math.Max(heatPumpHeat, 0);
            if (!climateControlEnabled || heatPumpLimit <= 0) return 0;

            ModuleSystemHeat heatModule = GetSystemHeatModule();
            if (!IsSystemHeatLoopAvailable(heatModule)) return 0;

            double heatMovedKW = heatPumpLimit;
            double wasteKW = Math.Max(heatPumpECRate, 0);
            return (float)(heatMovedKW + wasteKW);
        }

        void UpdateEditorWaterEvaporatorEstimate()
        {
            if (!HighLogic.LoadedSceneIsEditor || !waterEvaporatorAvailable) return;

            waterEvaporatorWaterRate = (float)(Math.Max(waterEvaporatorHeat, 0) / waterEvaporatorEnergyKJPerUnit);
        }

        void RefreshTemperatureReport()
        {
            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            if (scenario == null)
            {
                climateControlStatus = KickUIFormat.ReportLine(KickUIFormat.Good("Safe Temperature"));
                return;
            }

            LifeSupportStatus status = vessel != null ? scenario.GetData(vessel.id) : null;
            if (cabinTemp < 5f)
            {
                double remaining = status != null ? scenario.GraceTemp - status.tempRangeTime : scenario.GraceTemp;
                climateControlStatus = KickUIFormat.ReportLine(KickUIFormat.Bad($"Too Cold ({KickUIFormat.Timer(remaining)})"));
            }
            else if (cabinTemp > 45f)
            {
                double remaining = status != null ? scenario.GraceTemp - status.tempRangeTime : scenario.GraceTemp;
                climateControlStatus = KickUIFormat.ReportLine(KickUIFormat.Bad($"Too Hot ({KickUIFormat.Timer(remaining)})"));
            }
            else
            {
                climateControlStatus = KickUIFormat.ReportLine(KickUIFormat.Good("Safe Temperature"));
            }
        }

        double KToC(double k) { return k - 273.15; }
        double CToK(double c) { return c + 273.15; }

        void InitializeThermalStateIfNeeded()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;

            if (vessel != null && vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                cabinTemp = thermostatTemp;
                thermalStateInitialized = true;
                return;
            }

            bool invalidCabin = cabinTemp == 0 || cabinTemp < -200;

            if (invalidCabin)
            {
                cabinTemp = thermostatTemp;
            }

            thermalStateInitialized = true;
        }

        double GetHeatingToTargetKW(double currentTempK, double targetTempK, double heatCapacity, double dt)
        {
            if (currentTempK >= targetTempK || heatCapacity <= 0 || dt <= 0) return 0;

            return Math.Max(((targetTempK - currentTempK) * heatCapacity) / (1000.0 * dt), 0);
        }

        double GetCoolingToTargetKW(double currentTempK, double targetTempK, double heatCapacity, double dt)
        {
            if (currentTempK <= targetTempK || heatCapacity <= 0 || dt <= 0) return 0;

            return Math.Max(((currentTempK - targetTempK) * heatCapacity) / (1000.0 * dt), 0);
        }

        double Clamp01(double value)
        {
            if (value <= 0) return 0;
            if (value >= 1) return 1;
            return value;
        }

        double GetHeatPumpLoopFactor(double loopTemperatureK)
        {
            if (heatPumpLoopMaxTemp <= heatPumpLoopNominalTemp) return 1;
            if (loopTemperatureK <= heatPumpLoopNominalTemp) return 1;
            if (loopTemperatureK >= heatPumpLoopMaxTemp) return 0;

            return Clamp01(1.0 - ((loopTemperatureK - heatPumpLoopNominalTemp) / (heatPumpLoopMaxTemp - heatPumpLoopNominalTemp)));
        }

        double GetWaterEvaporatorPressureFactor(double pressureKPa)
        {
            if (pressureKPa <= waterEvaporatorFullPressureKPa) return 1;
            if (pressureKPa >= waterEvaporatorMaxPressureKPa) return 0;

            return Clamp01(1.0 - ((pressureKPa - waterEvaporatorFullPressureKPa) /
                (waterEvaporatorMaxPressureKPa - waterEvaporatorFullPressureKPa)));
        }

        double GetAirCoolingPressureFactor(double pressureKPa)
        {
            if (pressureKPa <= airCoolingMinPressureKPa) return 0;
            if (pressureKPa >= airCoolingFullPressureKPa) return 1;

            return Clamp01((pressureKPa - airCoolingMinPressureKPa) /
                (airCoolingFullPressureKPa - airCoolingMinPressureKPa));
        }

        void ResolveSystemHeatModule()
        {
            cachedSystemHeatModule = part != null ? ModuleUtils.FindHeatModule(part, heatPumpSystemHeatModuleID) : null;
        }

        void SyncSystemHeatAvailability()
        {
            ModuleSystemHeat heatModule = GetSystemHeatModule();
            if (heatModule == null || heatModule.moduleUsed == heatPumpAvailable) return;

            if (!heatPumpAvailable)
            {
                heatModule.AddFlux("kickHeatPump", 0f, 0f, false);
                systemHeatFluxEstimate = 0;
                isHeatPumpActive = false;
            }

            heatModule.SetSystemHeatModuleEnabled(heatPumpAvailable);
            lastEditorSystemHeatFlux = float.NaN;
            lastEditorSystemHeatSourceTemp = float.NaN;
        }

        ModuleSystemHeat GetSystemHeatModule()
        {
            return cachedSystemHeatModule;
        }

        bool IsSystemHeatLoopAvailable(ModuleSystemHeat heatModule)
        {
            return heatModule != null && heatModule.moduleUsed && heatModule.Loop != null;
        }

        bool HasSystemHeatRadiator(ModuleSystemHeat heatModule)
        {
            if (!IsSystemHeatLoopAvailable(heatModule)) return false;

            foreach (ModuleSystemHeat loopModule in heatModule.Loop.LoopModules)
            {
                if (loopModule?.part != null && loopModule.part.Modules.Contains("ModuleSystemHeatRadiator"))
                {
                    return true;
                }
            }

            return false;
        }

        double GetSystemHeatLoopTemperature(ModuleSystemHeat heatModule)
        {
            return heatModule != null ? heatModule.LoopTemperature : 0;
        }

        bool SetSystemHeatPumpFlux(double sourceTempK, double fluxKW, bool useForNominal)
        {
            ModuleSystemHeat heatModule = GetSystemHeatModule();
            if (heatModule == null || (!heatPumpAvailable && fluxKW > 0)) return false;

            heatModule.AddFlux("kickHeatPump", (float)sourceTempK, (float)fluxKW, useForNominal);
            debugHeatPumpLoopTemp = GetSystemHeatLoopTemperature(heatModule);
            return true;
        }

        void UpdateEditorSystemHeatEstimate()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            systemHeatFluxEstimate = GetEditorSystemHeatFluxEstimate();
            if (!IsSystemHeatLoopAvailable(GetSystemHeatModule())) return;

            float sourceTempK = (float)CToK(thermostatTemp);
            bool useForNominal = systemHeatFluxEstimate > 0;
            bool fluxChanged = float.IsNaN(lastEditorSystemHeatFlux) ||
                Math.Abs(systemHeatFluxEstimate - lastEditorSystemHeatFlux) > 0.0001f;
            bool sourceChanged = float.IsNaN(lastEditorSystemHeatSourceTemp) ||
                Math.Abs(sourceTempK - lastEditorSystemHeatSourceTemp) > 0.01f;

            if (!fluxChanged && !sourceChanged && useForNominal == lastEditorSystemHeatUseForNominal) return;

            if (SetSystemHeatPumpFlux(sourceTempK, systemHeatFluxEstimate, useForNominal))
            {
                lastEditorSystemHeatFlux = systemHeatFluxEstimate;
                lastEditorSystemHeatSourceTemp = sourceTempK;
                lastEditorSystemHeatUseForNominal = useForNominal;
            }
        }

        void RunThermalLogic(ref double cabinHeatFlux, ref double partHeatFlux, KickLifeSupportModule lifeSupport)
        {
            currentTemperatureControlECRate = 0;
            currentHeaterECRate = 0;
            currentEvaporatorECRate = 0;
            currentAirCoolingECRate = 0;
            currentHeatPumpECRate = 0;
            systemHeatFluxEstimate = 0;
            double currentCabinTempK = CToK(cabinTemp);
            double dt = TimeWarp.fixedDeltaTime;
            double targetTempK = CToK(thermostatTemp);
            isWaterEvaporatorActive = false;
            isAirCoolingActive = false;
            waterEvaporatorWaterRate = 0;

            double systemEC = systemECRate * TimeWarp.fixedDeltaTime;
            bool environmentalControlPowered = false;

            if (TimeWarp.CurrentRate > 100f)
            {
                SetSystemHeatPumpFlux(0, 0, false);
                if (heatPumpAvailable && heatPumpEnabled && cabinTemp > thermostatTemp &&
                    !HasSystemHeatRadiator(GetSystemHeatModule()))
                {
                    heatPumpStatus = "No Radiator";
                    return;
                }

                if (climateControlEnabled)
                {
                    if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
                    {
                        currentTemperatureControlECRate += Math.Max(systemECRate, 0);
                        bool canHeat = heaterEnabled && cabinTemp < thermostatTemp;
                        bool canCool =
                            (waterEvaporatorAvailable && evaporatorEnabled) ||
                            (airCoolingAvailable && airCoolingEnabled) ||
                            (heatPumpAvailable && heatPumpEnabled);
                        if (canHeat || (canCool && cabinTemp > thermostatTemp))
                        {
                            cabinTemp = thermostatTemp;
                        }
                    }
                }

                return;
            }

            if (climateControlEnabled)
            {
                if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
                {
                    currentTemperatureControlECRate += Math.Max(systemECRate, 0);
                    environmentalControlPowered = true;
                    debugSystemHeat = systemHeat;
                    cabinHeatFlux += systemHeat;
                    climateControlStatus = "On";
                    isHeaterActive = false;
                    heaterStatus = "Standby";
                    isHeatPumpActive = false;
                    heatPumpStatus = "Standby";
                    waterEvaporatorStatus = "Standby";
                    airCoolingStatus = "Standby";
                }
                else
                {
                    climateControlStatus = "No Power";
                    heaterStatus = "See CC Status";
                    heatPumpStatus = "See CC Status";
                    waterEvaporatorStatus = "See CC Status";
                    airCoolingStatus = "See CC Status";
                    isHeaterActive = false;
                    isHeatPumpActive = false;
                    isWaterEvaporatorActive = false;
                    isAirCoolingActive = false;
                }
            }
            else
            {
                climateControlStatus = "Disabled";
                heaterStatus = "Disabled";
                heatPumpStatus = "Disabled";
                waterEvaporatorStatus = "Disabled";
                airCoolingStatus = "Disabled";
                isHeaterActive = false;
                isHeatPumpActive = false;
                isWaterEvaporatorActive = false;
                isAirCoolingActive = false;
            }

            double activeChange = 0;
            double cabinHeatCapacity = GetCabinHeatCapacity();
            double passiveChange = 0;
            double hullTemp = part.temperature;

            if (lifeSupport != null &&
                lifeSupport.atmosphereControlMode == KickLifeSupportModule.AtmosphereControlNone)
            {
                debugCabinPartConductance = unpressurizedCabinPartConductanceKWPerK;
            }
            else if (lifeSupport != null &&
                     KickLifeSupportModule.UsesPartialPressurization(lifeSupport.atmosphereControlMode))
            {
                debugCabinPartConductance = partiallyPressurizedCabinPartConductanceKWPerK;
            }
            else
            {
                debugCabinPartConductance = insulatedCabinPartConductanceKWPerK;
            }

            if (dt > 0 && cabinHeatCapacity > 0)
            {
                debugCabinToPartKW = (currentCabinTempK - hullTemp) * debugCabinPartConductance;

                passiveChange = -(debugCabinToPartKW * 1000.0 * dt) / cabinHeatCapacity;
                currentCabinTempK += passiveChange;
                partHeatFlux += debugCabinToPartKW;
            }

            if (environmentalControlPowered && dt > 0)
            {
                double projectedCabinTempK = currentCabinTempK;

                if (cabinHeatCapacity > 0)
                {
                    projectedCabinTempK += (cabinHeatFlux * 1000.0 * dt) / cabinHeatCapacity;
                }

                double heaterLimitKW = heaterEnabled ? Math.Max(heaterHeat, 0) : 0;
                double requestedHeaterKW = GetHeatingToTargetKW(projectedCabinTempK, targetTempK, cabinHeatCapacity, dt);
                double heaterKW = Math.Min(heaterLimitKW, requestedHeaterKW);

                if (heaterKW > 0.000001 && heaterLimitKW > 0)
                {
                    double heaterFraction = Math.Min(heaterKW / heaterLimitKW, 1.0);
                    double heaterEC = heaterLimitKW * heaterFraction * dt;
                    if (part.RequestResource(ecId, heaterEC) < heaterEC * 0.99)
                    {
                        heaterStatus = "No Power";
                    }
                    else
                    {
                        currentTemperatureControlECRate += heaterKW;
                        currentHeaterECRate = heaterKW;
                        isHeaterActive = true;
                        debugHeaterHeat = heaterKW;
                        heaterOutput = (float)heaterKW;
                        cabinHeatFlux += heaterKW;
                        projectedCabinTempK += (heaterKW * 1000.0 * dt) / cabinHeatCapacity;
                        heaterStatus = requestedHeaterKW > heaterLimitKW + 0.000001 ? "Limited" : "Active";
                    }
                }

                double requestedActiveCoolingKW = GetCoolingToTargetKW(projectedCabinTempK, targetTempK, cabinHeatCapacity, dt);
                if (waterEvaporatorAvailable && evaporatorEnabled)
                {
                    SetSystemHeatPumpFlux(0, 0, false);
                    systemHeatFluxEstimate = 0;

                    if (requestedActiveCoolingKW > 0.000001 && cabinHeatCapacity > 0)
                    {
                        double pressureKPa = vessel != null ? Math.Max(vessel.staticPressurekPa, 0) : 0;
                        double pressureFactor = GetWaterEvaporatorPressureFactor(pressureKPa);
                        debugWaterEvaporatorPressureFactor = pressureFactor;

                        if (pressureFactor <= 0)
                        {
                            waterEvaporatorStatus = "Atmosphere";
                        }
                        else
                        {
                            double evaporatorLimitKW = Math.Max(waterEvaporatorHeat, 0) * pressureFactor;
                            double requestedGrossCoolingKW = requestedActiveCoolingKW / waterEvaporatorTransferEfficiency;
                            double desiredGrossCoolingKW = Math.Min(evaporatorLimitKW, requestedGrossCoolingKW);
                            evaporatorEnvironmentLimited = pressureFactor < 0.999;
                            evaporatorAtSetLimit =
                                pressureFactor >= 0.999 &&
                                requestedGrossCoolingKW > evaporatorLimitKW + 0.000001;

                            if (desiredGrossCoolingKW > 0.000001 && waterEvaporatorHeat > 0)
                            {
                                double requestedEnergyKJ = desiredGrossCoolingKW * dt;
                                double availableWater = 0;
                                double maxWater = 0;
                                double availableWasteWater = 0;
                                double maxWasteWater = 0;
                                double storedWaste = 0;
                                double maxWaste = 0;
                                if (waterId != -1 && vessel != null)
                                {
                                    vessel.GetConnectedResourceTotals(waterId, out availableWater, out maxWater);
                                }
                                if (wasteWaterId != -1 && vessel != null)
                                {
                                    vessel.GetConnectedResourceTotals(wasteWaterId, out availableWasteWater, out maxWasteWater);
                                }
                                if (wasteId != -1 && vessel != null)
                                {
                                    vessel.GetConnectedResourceTotals(wasteId, out storedWaste, out maxWaste);
                                }
                                debugWaterAvailable = availableWater;
                                debugWasteWaterAvailable = availableWasteWater;

                                double availableWasteCapacity = Math.Max(maxWaste - storedWaste, 0);
                                double maxWasteWaterByResidue = wasteWaterResidueUnitsPerUnit > 0
                                    ? availableWasteCapacity / wasteWaterResidueUnitsPerUnit
                                    : 0;
                                double usableWasteWater = Math.Min(availableWasteWater, maxWasteWaterByResidue);
                                double wasteWaterEnergyKJPerUnit =
                                    waterEvaporatorEnergyKJPerUnit * wasteWaterCoolingFactor;
                                bool allowWasteWater = evaporatorFeedMode != "Fresh Only";
                                bool allowFreshWater = evaporatorFeedMode != "Waste Only";
                                bool freshWaterFirst =
                                    evaporatorFeedMode == "Fresh First" ||
                                    evaporatorFeedMode == "Fresh Only";
                                double wasteWaterToConsume = 0;
                                double waterToConsume = 0;
                                double remainingEnergyKJ = requestedEnergyKJ;

                                if (freshWaterFirst && allowFreshWater)
                                {
                                    waterToConsume = Math.Min(
                                        availableWater,
                                        remainingEnergyKJ / waterEvaporatorEnergyKJPerUnit);
                                    remainingEnergyKJ -= waterToConsume * waterEvaporatorEnergyKJPerUnit;
                                }

                                if (allowWasteWater && wasteWaterEnergyKJPerUnit > 0)
                                {
                                    wasteWaterToConsume = Math.Min(
                                        usableWasteWater,
                                        remainingEnergyKJ / wasteWaterEnergyKJPerUnit);
                                    remainingEnergyKJ -= wasteWaterToConsume * wasteWaterEnergyKJPerUnit;
                                }

                                if (!freshWaterFirst && allowFreshWater)
                                {
                                    waterToConsume = Math.Min(
                                        availableWater,
                                        Math.Max(remainingEnergyKJ, 0) / waterEvaporatorEnergyKJPerUnit);
                                }

                                double wasteWaterEnergyKJ = wasteWaterToConsume * wasteWaterEnergyKJPerUnit;
                                double supportedEnergyKJ =
                                    wasteWaterEnergyKJ + (waterToConsume * waterEvaporatorEnergyKJPerUnit);
                                double potentialGrossCoolingKW = supportedEnergyKJ / dt;
                                evaporatorResourceLimited =
                                    supportedEnergyKJ < requestedEnergyKJ * 0.999;

                                if (potentialGrossCoolingKW <= 0.000001)
                                {
                                    waterEvaporatorStatus = "No Water";
                                }
                                else
                                {
                                    double transferFraction = Clamp01(potentialGrossCoolingKW / waterEvaporatorHeat);
                                    double evaporatorECPerSecond = Math.Max(waterEvaporatorECRate, 0) * transferFraction;
                                    double requestedEC = evaporatorECPerSecond * dt;
                                    double consumedEC = requestedEC > 0 ? part.RequestResource(ecId, requestedEC) : 0;

                                    if (requestedEC > 0 && consumedEC < requestedEC * 0.99)
                                    {
                                        waterEvaporatorStatus = "No Power";
                                    }
                                    else
                                    {
                                        double consumedWasteWater = wasteWaterToConsume > 0
                                            ? part.RequestResource(wasteWaterId, wasteWaterToConsume)
                                            : 0;
                                        double consumedWater = waterToConsume > 0
                                            ? part.RequestResource(waterId, waterToConsume)
                                            : 0;
                                        double wasteProduced = consumedWasteWater * wasteWaterResidueUnitsPerUnit;
                                        if (wasteProduced > 0)
                                        {
                                            part.RequestResource(wasteId, -wasteProduced);
                                        }

                                        double actualEnergyKJ =
                                            (consumedWasteWater * wasteWaterEnergyKJPerUnit) +
                                            (consumedWater * waterEvaporatorEnergyKJPerUnit);
                                        double actualGrossCoolingKW = actualEnergyKJ / dt;
                                        double actualCoolingKW = actualGrossCoolingKW * waterEvaporatorTransferEfficiency;

                                        if (actualCoolingKW <= 0.000001)
                                        {
                                            waterEvaporatorStatus = "No Water";
                                        }
                                        else
                                        {
                                            currentTemperatureControlECRate += evaporatorECPerSecond;
                                            currentEvaporatorECRate = evaporatorECPerSecond;
                                            isWaterEvaporatorActive = true;
                                            debugWaterEvaporatorHeat = actualCoolingKW;
                                            evaporatorOutput = (float)actualCoolingKW;
                                            debugWaterEvaporatorRate = (consumedWasteWater + consumedWater) / dt;
                                            debugWasteWaterRate = consumedWasteWater / dt;
                                            debugWasteProducedRate = wasteProduced / dt;
                                            waterEvaporatorWaterRate = (float)debugWaterEvaporatorRate;
                                            cabinHeatFlux -= actualCoolingKW;
                                            projectedCabinTempK -= (actualCoolingKW * 1000.0 * dt) / cabinHeatCapacity;
                                            waterEvaporatorStatus =
                                                actualCoolingKW < requestedActiveCoolingKW - 0.000001 ||
                                                pressureFactor < 0.999 ||
                                                supportedEnergyKJ < requestedEnergyKJ * 0.999 ||
                                                actualEnergyKJ < supportedEnergyKJ * 0.999
                                                    ? "Limited"
                                                    : "Active";
                                        }
                                    }
                                }
                            }
                            else if (waterEvaporatorHeat <= 0)
                            {
                                waterEvaporatorStatus = "Standby";
                            }
                        }
                    }
                }
                else if (airCoolingAvailable && airCoolingEnabled)
                {
                    SetSystemHeatPumpFlux(0, 0, false);
                    systemHeatFluxEstimate = 0;

                    if (requestedActiveCoolingKW > 0.000001 && cabinHeatCapacity > 0)
                    {
                        double pressureKPa = vessel != null ? Math.Max(vessel.staticPressurekPa, 0) : 0;
                        double pressureFactor = GetAirCoolingPressureFactor(pressureKPa);
                        debugAirCoolingPressureFactor = pressureFactor;

                        if (pressureFactor <= 0)
                        {
                            airCoolingStatus = "No Atmosphere";
                        }
                        else
                        {
                            double coolingLimitKW = Math.Max(airCoolingHeat, 0) * pressureFactor;
                            double coolingKW = Math.Min(coolingLimitKW, requestedActiveCoolingKW);
                            airCoolingEnvironmentLimited = pressureFactor < 0.999;
                            airCoolingAtSetLimit =
                                pressureFactor >= 0.999 &&
                                requestedActiveCoolingKW > coolingLimitKW + 0.000001;

                            if (coolingKW > 0.000001 && airCoolingHeat > 0)
                            {
                                double transferFraction = Clamp01(coolingKW / airCoolingHeat);
                                double requestedEC = Math.Max(airCoolingECRate, 0) * transferFraction * dt;
                                double consumedEC = requestedEC > 0 ? part.RequestResource(ecId, requestedEC) : 0;

                                if (requestedEC > 0 && consumedEC < requestedEC * 0.99)
                                {
                                    airCoolingStatus = "No Power";
                                }
                                else
                                {
                                    currentTemperatureControlECRate += Math.Max(airCoolingECRate, 0) * transferFraction;
                                    currentAirCoolingECRate = Math.Max(airCoolingECRate, 0) * transferFraction;
                                    isAirCoolingActive = true;
                                    debugAirCoolingHeat = coolingKW;
                                    airCoolingOutput = (float)coolingKW;
                                    cabinHeatFlux -= coolingKW;
                                    projectedCabinTempK -= (coolingKW * 1000.0 * dt) / cabinHeatCapacity;
                                    airCoolingStatus =
                                        coolingKW < requestedActiveCoolingKW - 0.000001 ||
                                        pressureFactor < 0.999
                                            ? "Limited"
                                            : "Active";
                                }
                            }
                        }
                    }
                }
                else if (heatPumpAvailable && heatPumpEnabled)
                {
                    if (requestedActiveCoolingKW > 0.000001 && cabinHeatCapacity > 0)
                    {
                        ModuleSystemHeat heatModule = GetSystemHeatModule();
                        if (heatModule == null)
                        {
                            heatPumpStatus = "No SystemHeat";
                            SetSystemHeatPumpFlux(0, 0, false);
                        }
                        else if (!IsSystemHeatLoopAvailable(heatModule))
                        {
                            heatPumpStatus = "No Loop";
                            SetSystemHeatPumpFlux(0, 0, false);
                        }
                        else if (!HasSystemHeatRadiator(heatModule))
                        {
                            heatPumpStatus = "No Radiator";
                            SetSystemHeatPumpFlux(0, 0, false);
                        }
                        else
                        {
                            debugHeatPumpLoopTemp = GetSystemHeatLoopTemperature(heatModule);
                            double loopFactor = GetHeatPumpLoopFactor(debugHeatPumpLoopTemp);
                            if (loopFactor <= 0)
                            {
                                heatPumpStatus = "Loop Hot";
                                SetSystemHeatPumpFlux(0, 0, false);
                            }
                            else
                            {
                                double heatPumpLimitKW = Math.Max(heatPumpHeat, 0) * loopFactor;
                                double transferKW = Math.Min(heatPumpLimitKW, requestedActiveCoolingKW);
                                heatPumpEnvironmentLimited = loopFactor < 0.999;
                                heatPumpAtSetLimit =
                                    loopFactor >= 0.999 &&
                                    requestedActiveCoolingKW > heatPumpLimitKW + 0.000001;

                                if (transferKW > 0.000001 && heatPumpHeat > 0)
                                {
                                    double transferFraction = Math.Min(transferKW / heatPumpHeat, 1.0);
                                    double heatPumpEC = heatPumpECRate * transferFraction * dt;
                                    if (part.RequestResource(ecId, heatPumpEC) < heatPumpEC * 0.99)
                                    {
                                        heatPumpStatus = "No Power";
                                        SetSystemHeatPumpFlux(0, 0, false);
                                    }
                                    else
                                    {
                                        double wasteHeatKW = heatPumpECRate * transferFraction;
                                        if (SetSystemHeatPumpFlux(projectedCabinTempK, transferKW + wasteHeatKW, true))
                                        {
                                            currentTemperatureControlECRate += heatPumpECRate * transferFraction;
                                            currentHeatPumpECRate = heatPumpECRate * transferFraction;
                                            isHeatPumpActive = true;
                                            debugHeatPumpHeat = transferKW;
                                            heatPumpOutput = (float)transferKW;
                                            debugHeatPumpWasteHeat = wasteHeatKW;
                                            systemHeatFluxEstimate = (float)(transferKW + wasteHeatKW);
                                            cabinHeatFlux -= transferKW;
                                            projectedCabinTempK -= (transferKW * 1000.0 * dt) / cabinHeatCapacity;
                                            heatPumpStatus = transferKW < requestedActiveCoolingKW - 0.000001 || loopFactor < 0.999 ? "Limited" : "Active";
                                        }
                                        else
                                        {
                                            heatPumpStatus = "No SystemHeat";
                                        }
                                    }
                                }
                                else
                                {
                                    SetSystemHeatPumpFlux(0, 0, false);
                                }
                            }
                        }
                    }
                    else
                    {
                        SetSystemHeatPumpFlux(0, 0, false);
                        systemHeatFluxEstimate = 0;
                    }
                }
                else
                {
                    SetSystemHeatPumpFlux(0, 0, false);
                    systemHeatFluxEstimate = 0;
                }

            }
            else
            {
                SetSystemHeatPumpFlux(0, 0, false);
                systemHeatFluxEstimate = 0;
            }

            debugCabinHeatCapacity = cabinHeatCapacity;

            if (Math.Abs(cabinHeatFlux) > 0.00001 && cabinHeatCapacity > 0)
            {
                double energyJoules = (cabinHeatFlux * 1000.0) * dt;
                activeChange = energyJoules / cabinHeatCapacity;
                currentCabinTempK += activeChange;
            }
            debugPassiveChangeK = passiveChange;
            debugActiveChangeK = activeChange;

            cabinTemp = (float)KToC(currentCabinTempK);

            passiveFlux = (float)(-debugCabinToPartKW);
        }

        double GetCabinHeatCapacity()
        {
            double airMass = part.CrewCapacity * KickLifeSupportModule.airPerSeat * airDensity;
            float baseMass = part.partInfo != null && part.partInfo.partPrefab != null
                ? part.partInfo.partPrefab.mass
                : part.mass;
            float moduleMass = part.GetModuleMass(baseMass, ModifierStagingSituation.CURRENT);
            double partMassKg = Math.Max(baseMass + moduleMass, 0) * 1000.0;
            return (airMass * airSpecificHeat) +
                (partMassKg * effectiveCabinPartMassFraction * genericCabinSpecificHeat);
        }

        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            cachedAnalyticTemp = toBeInternal;
            LogAnalyticThermalDebug(analyticTemp, toBeInternal, toBeSkin);
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = false;
            return -1;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = false;
            return -1;
        }

        void LogThermalDebug()
        {
            if (!ShouldLogThermalDebug()) return;

            double now = Time.realtimeSinceStartup;
            lastThermalLogTime = now;
            double partTempC = KToC(part.temperature);
            double pressureKpa = vessel != null ? vessel.staticPressurekPa : 0;
            double ecAvailable = 0;

            if (ecId != -1)
            {
                PartResource ec = part.Resources.Get(ecId);
                if (ec != null) ecAvailable = ec.amount;
            }

            Debug.Log(
                $"[KICKLS][THERMAL] {vessel?.vesselName ?? "No Vessel"} / {part.partInfo?.title ?? part.name} | " +
                $"cabin={cabinTemp:F2}C part={partTempC:F2}C thermostat={thermostatTemp:F1}C pressure={pressureKpa:F3}kPa warp={TimeWarp.CurrentRate:F1} " +
                $"cabinHeat={heatFlux:F3}kW partHeat={debugPartHeat:F3}kW passive={passiveFlux:F3}kW cabinToPart={debugCabinToPartKW:F4}kW stockFlux={debugStockPartFluxKW:F4}kW " +
                $"sources(av={debugAvionicsHeat:F3}, acs={debugLifeSupportHeat:F3}, crew={debugCrewHeat:F3}, encon={debugSystemHeat:F3}, heater={debugHeaterHeat:F3}, evaporator={debugWaterEvaporatorHeat:F3}, water={debugWaterAvailable:F3}, wasteWater={debugWasteWaterAvailable:F3}, feedRate={debugWaterEvaporatorRate:F5}/s, wasteWaterRate={debugWasteWaterRate:F5}/s, wasteRate={debugWasteProducedRate:F5}/s, evapPressure={debugWaterEvaporatorPressureFactor:F2}, airCooling={debugAirCoolingHeat:F3}, airPressure={debugAirCoolingPressureFactor:F2}, pump={debugHeatPumpHeat:F3}, pumpWaste={debugHeatPumpWasteHeat:F3}, pumpLoop={debugHeatPumpLoopTemp:F1}K)kW " +
                $"dT(passive={debugPassiveChangeK:F4}K, active={debugActiveChangeK:F4}K) cabinCapacity={debugCabinHeatCapacity / 1000.0:F2}kJ/K conductance(cabinPart={debugCabinPartConductance:F3})kW/K " +
                $"encon={climateControlEnabled} heater={heaterStatus} evaporator={waterEvaporatorStatus} airCooling={airCoolingStatus} heatPump={heatPumpStatus} EC(part)={ecAvailable:F3}");
        }

        void LogAnalyticThermalDebug(double analyticTemp, double toBeInternal, double toBeSkin)
        {
            if (!ShouldLogThermalDebug()) return;

            double now = Time.realtimeSinceStartup;
            if (now - lastAnalyticLogTime < 15.0) return;

            lastAnalyticLogTime = now;
            Debug.Log(
                $"[KICKLS][THERMAL][ANALYTIC] {vessel?.vesselName ?? "No Vessel"} / {part.partInfo?.title ?? part.name} | " +
                $"analytic={KToC(analyticTemp):F2}C internal={KToC(toBeInternal):F2}C skin={KToC(toBeSkin):F2}C " +
                $"cachedInternal={KToC(cachedAnalyticTemp):F2}C cabin={cabinTemp:F2}C encon={climateControlEnabled}");
        }

        bool ShouldLogThermalDebug()
        {
            if (!debugThermalLogging || !HighLogic.LoadedSceneIsFlight) return false;
            if (vessel == null || FlightGlobals.ActiveVessel == null || vessel != FlightGlobals.ActiveVessel) return false;

            double now = Time.realtimeSinceStartup;
            return now - lastThermalLogTime >= 5.0;
        }
    }
}

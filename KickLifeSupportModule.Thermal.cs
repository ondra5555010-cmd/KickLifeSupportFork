using System;
using SystemHeat;
using UnityEngine;

namespace KickLifeSupport
{
    public partial class KickLifeSupportModule : IAnalyticTemperatureModifier
    {
        KickLifeSupportSettings gameSettings;

        #region Thermal GUI Fields
        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Situation Report", groupName = "KICKTEMP", groupDisplayName = "EnCon - General")]
        public string climateControlStatus = "On";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Master Switch", groupName = "KICKTEMP", groupDisplayName = "EnCon - General"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool climateControlEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "EC Limit", groupName = "KICKTEMP", groupDisplayName = "EnCon - General", guiFormat = "F3", guiUnits = " EC/s")]
        [UI_FloatRange(minValue = 0f, maxValue = 1f, stepIncrement = 0.01f, scene = UI_Scene.All)]
        public float enconECLimit = 1f;

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Thermostat", groupName = "KICKTEMP", groupDisplayName = "EnCon - General")]
        [UI_FloatRange(minValue = 10f, maxValue = 30f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float thermostatTemp = 22f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Cabin Temperature", groupName = "KICKTEMP", groupDisplayName = "EnCon - General", guiFormat = "F1", guiUnits = " C")]
        public float cabinTemp = 0f;

        [KSPField(isPersistant = true)]
        public bool thermalStateInitialized = false;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Part Temperature", groupName = "KICKTEMP", groupDisplayName = "EnCon - General", guiFormat = "F1", guiUnits = " C")]
        public float partTempDisplay = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Cabin-Part Flux", groupName = "KICKFLUX", groupDisplayName = "EnCon - Flux", groupStartCollapsed = true, guiFormat = "F3", guiUnits = " kW")]
        public float cabinPartFlux = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Onboard Heat", groupName = "KICKFLUX", groupDisplayName = "EnCon - Flux", guiFormat = "F3", guiUnits = " kW")]
        public float onboardHeatFlux = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Active Control", groupName = "KICKFLUX", groupDisplayName = "EnCon - Flux", guiFormat = "F3", guiUnits = " kW")]
        public float activeControlFlux = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Net Flux", groupName = "KICKFLUX", groupDisplayName = "EnCon - Flux", guiFormat = "F3", guiUnits = " kW")]
        public float effectiveFlux = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Heater", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool heaterEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Heat Created", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems", guiFormat = "F3", guiUnits = " kW")]
        public float heaterOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Evaporator", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool evaporatorEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Evaporator Feed", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems")]
        [UI_ChooseOption(scene = UI_Scene.All, options = new[] { "Waste First", "Waste Only", "Fresh First", "Fresh Only" })]
        public string evaporatorFeedMode = "Waste First";

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Water Limit", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems", guiFormat = "F6", guiUnits = " L/s")]
        [UI_FloatRange(minValue = 0f, maxValue = 0.001f, stepIncrement = 0.00001f, scene = UI_Scene.All)]
        public float waterEvaporatorWaterLimit = 0.000435f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Heat Removed", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems", guiFormat = "F3", guiUnits = " kW")]
        public float evaporatorOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Air Cooling", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool airCoolingEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Heat Removed", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems", guiFormat = "F3", guiUnits = " kW")]
        public float airCoolingOutput = 0f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Heat Pump", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool heatPumpEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Heat Removed", groupName = "KICKTHERM", groupDisplayName = "EnCon - Systems", guiFormat = "F3", guiUnits = " kW")]
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
        [KSPField] public float enconMaxECRate = 1f;
        [KSPField] public float heaterMaxECRate = 1f;
        [KSPField] public float heaterHeatPerEC = 1f;
        [KSPField] public float heatPumpMaxECRate = 0.1f;
        [KSPField] public float heatPumpHeatPerEC = 10f;
        [KSPField] public float waterEvaporatorMaxECRate = 0.01f;
        [KSPField] public float waterEvaporatorHeatPerEC = 100f;
        [KSPField] public float waterEvaporatorMaxWaterRate = 0.000435f;
        [KSPField] public float waterEvaporatorEfficiency = 0.9f;
        [KSPField] public float airCoolingMaxECRate = 0.05f;
        [KSPField] public float airCoolingHeatPerEC = 5f;
        [KSPField] public float heatPumpLoopNominalTemp = 320f;
        [KSPField] public float heatPumpLoopMaxTemp = 370f;
        [KSPField] public string heatPumpSystemHeatModuleID = "kickECS";
        [KSPField] public bool heatPumpAvailable = true;
        [KSPField] public bool waterEvaporatorAvailable = false;
        [KSPField] public bool airCoolingAvailable = false;
        [KSPField(guiName = "Coolant Loop Load", guiFormat = "F3", guiUnits = " kW")]
        public float systemHeatFluxEstimate = 0f;
        #endregion

        public bool isHeaterActive = false;
        public bool isHeatPumpActive = false;
        public bool isWaterEvaporatorActive = false;
        public bool isAirCoolingActive = false;
        int waterId = -1;
        int wasteWaterId = -1;
        double wasteWaterResidueUnitsPerUnit;
        double cachedAnalyticTemp;
        double currentTemperatureControlECRate;
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
        bool evaporatorEquipmentLimited;
        bool evaporatorEnvironmentLimited;
        bool evaporatorResourceLimited;
        bool evaporatorWaterLimited;
        bool airCoolingAtSetLimit;
        bool airCoolingEquipmentLimited;
        bool airCoolingEnvironmentLimited;
        bool heatPumpAtSetLimit;
        bool heatPumpEquipmentLimited;
        bool heatPumpEnvironmentLimited;
        double configuredAirDensity;
        double configuredAirSpecificHeat;
        double configuredGenericCabinSpecificHeat;
        double configuredWasteWaterCoolingFactor;
        double configuredWasteWaterResidueMassFraction;
        double configuredEvaporationEnergyKJPerUnit;
        double configuredWaterEvaporatorFullPressureKPa;
        double configuredWaterEvaporatorMaxPressureKPa;
        double configuredAirCoolingMinPressureKPa;
        double configuredAirCoolingFullPressureKPa;
        float configuredHighWarpStabilizationThreshold;
        float configuredMinSafeCabinTemp;
        float configuredMaxSafeCabinTemp;
        float configuredMinWarningCabinTemp;
        float configuredMaxWarningCabinTemp;

        void ThermalOnStart(StartState state)
        {
            gameSettings = HighLogic.CurrentGame.Parameters.CustomParams<KickLifeSupportSettings>();
            NormalizeEvaporatorFeedMode();
            LoadThermalSettings();

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
                    wasteWaterDef.density * configuredWasteWaterResidueMassFraction / wasteDef.density;
            }

            InitializeThermalStateIfNeeded();
            ResolveSystemHeatModule();
            SyncSystemHeatAvailability();

            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        void LoadThermalSettings()
        {
            configuredAirDensity =
                KickLifeSupportConfig.GetDouble("AIR_DENSITY", 0.001225);
            configuredAirSpecificHeat =
                KickLifeSupportConfig.GetDouble("AIR_SPECIFIC_HEAT", 1005);
            configuredGenericCabinSpecificHeat =
                KickLifeSupportConfig.GetDouble("GENERIC_CABIN_SPECIFIC_HEAT", 1000);
            configuredWasteWaterCoolingFactor =
                KickLifeSupportConfig.GetDouble("WASTEWATER_COOLING_FACTOR", 0.8);
            configuredWasteWaterResidueMassFraction =
                KickLifeSupportConfig.GetDouble("WASTEWATER_RESIDUE_MASS_FRACTION", 0.2);
            configuredEvaporationEnergyKJPerUnit =
                KickLifeSupportConfig.GetDouble("EVAPORATION_ENERGY_KJ_PER_UNIT", 2300);
            configuredWaterEvaporatorFullPressureKPa =
                KickLifeSupportConfig.GetDouble("WATER_EVAPORATOR_FULL_PRESSURE", 0.5);
            configuredWaterEvaporatorMaxPressureKPa =
                KickLifeSupportConfig.GetDouble("WATER_EVAPORATOR_MAX_PRESSURE", 2.5);
            configuredAirCoolingMinPressureKPa =
                KickLifeSupportConfig.GetDouble("AIR_COOLING_MIN_PRESSURE", 1.0);
            configuredAirCoolingFullPressureKPa =
                KickLifeSupportConfig.GetDouble("AIR_COOLING_FULL_PRESSURE", 20.0);
            configuredHighWarpStabilizationThreshold =
                KickLifeSupportConfig.GetFloat("HIGH_WARP_STABILIZATION_THRESHOLD", 100f);
            configuredMinSafeCabinTemp =
                KickLifeSupportConfig.GetFloat("MIN_SAFE_CABIN_TEMP", 5f);
            configuredMaxSafeCabinTemp =
                KickLifeSupportConfig.GetFloat("MAX_SAFE_CABIN_TEMP", 45f);
            configuredMinWarningCabinTemp =
                KickLifeSupportConfig.GetFloat("MIN_WARNING_CABIN_TEMP", 10f);
            configuredMaxWarningCabinTemp =
                KickLifeSupportConfig.GetFloat("MAX_WARNING_CABIN_TEMP", 40f);

            configuredAirDensity = Math.Max(configuredAirDensity, 0);
            configuredAirSpecificHeat = Math.Max(configuredAirSpecificHeat, 0);
            configuredGenericCabinSpecificHeat =
                Math.Max(configuredGenericCabinSpecificHeat, 0);
            configuredWasteWaterCoolingFactor =
                Math.Max(configuredWasteWaterCoolingFactor, 0);
            configuredWasteWaterResidueMassFraction =
                Math.Max(configuredWasteWaterResidueMassFraction, 0);
            configuredEvaporationEnergyKJPerUnit =
                Math.Max(configuredEvaporationEnergyKJPerUnit, 0.000001);
            configuredWaterEvaporatorMaxPressureKPa = Math.Max(
                configuredWaterEvaporatorMaxPressureKPa,
                configuredWaterEvaporatorFullPressureKPa + 0.000001);
            configuredAirCoolingFullPressureKPa = Math.Max(
                configuredAirCoolingFullPressureKPa,
                configuredAirCoolingMinPressureKPa + 0.000001);

            float thermostatMin =
                KickLifeSupportConfig.GetFloat("THERMOSTAT_MIN", 10f);
            float thermostatMax =
                KickLifeSupportConfig.GetFloat("THERMOSTAT_MAX", 30f);
            ConfigureFloatRange(
                "thermostatTemp",
                thermostatMin,
                thermostatMax,
                KickLifeSupportConfig.GetFloat("THERMOSTAT_STEP", 0.5f));
            ConfigureFloatRange(
                "enconECLimit",
                0,
                Math.Max(enconMaxECRate, 0),
                KickLifeSupportConfig.GetFloat("ENCON_EC_LIMIT_STEP", 0.001f));
            ConfigureFloatRange(
                "waterEvaporatorWaterLimit",
                0,
                Math.Max(waterEvaporatorMaxWaterRate, 0),
                KickLifeSupportConfig.GetFloat(
                    "EVAPORATOR_WATER_LIMIT_STEP",
                    0.000005f));

            thermostatTemp = Mathf.Clamp(thermostatTemp, thermostatMin, thermostatMax);
            enconECLimit = Mathf.Clamp(
                enconECLimit,
                0,
                GetFloatRangeMaximum("enconECLimit", enconMaxECRate));
            waterEvaporatorWaterLimit = Mathf.Clamp(
                waterEvaporatorWaterLimit,
                0,
                GetFloatRangeMaximum(
                    "waterEvaporatorWaterLimit",
                    waterEvaporatorMaxWaterRate));
        }

        void ConfigureFloatRange(
            string fieldName,
            float minimum,
            float maximum,
            float step = 0)
        {
            maximum = Mathf.Max(maximum, minimum);
            BaseField field = Fields[fieldName];
            UI_FloatRange editor = field.uiControlEditor as UI_FloatRange;
            UI_FloatRange flight = field.uiControlFlight as UI_FloatRange;
            if (editor != null)
            {
                editor.minValue = minimum;
                editor.maxValue = maximum;
                if (step > 0) editor.stepIncrement = step;
            }
            if (flight != null)
            {
                flight.minValue = minimum;
                flight.maxValue = maximum;
                if (step > 0) flight.stepIncrement = step;
            }
        }

        float GetFloatRangeMaximum(string fieldName, float fallback)
        {
            BaseField field = Fields[fieldName];
            UI_FloatRange editor = field.uiControlEditor as UI_FloatRange;
            UI_FloatRange flight = field.uiControlFlight as UI_FloatRange;
            if (HighLogic.LoadedSceneIsEditor && editor != null) return editor.maxValue;
            return flight != null ? flight.maxValue : fallback;
        }

        void ThermalOnUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor) return;

            SyncSystemHeatAvailability();
            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        void ThermalUpdate()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            SyncSystemHeatAvailability();
            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
            UpdateEditorSystemHeatEstimate();
            UpdateEditorWaterEvaporatorEstimate();
        }

        void ThermalFixedUpdate()
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
            evaporatorEquipmentLimited = false;
            evaporatorEnvironmentLimited = false;
            evaporatorResourceLimited = false;
            evaporatorWaterLimited = false;
            airCoolingAtSetLimit = false;
            airCoolingEquipmentLimited = false;
            airCoolingEnvironmentLimited = false;
            heatPumpAtSetLimit = false;
            heatPumpEquipmentLimited = false;
            heatPumpEnvironmentLimited = false;

            KickAvionicsModule avionics = part.FindModuleImplementing<KickAvionicsModule>();
            if (avionics != null)
            {
                debugAvionicsHeat = avionics.currentHeatFlux;
                cabinHeatFlux += debugAvionicsHeat;
            }

            KickLifeSupportModule lifeSupport = this;
            debugLifeSupportHeat = currentHeatFlux;
            cabinHeatFlux += currentSystemHeatFlux;
            cabinHeatFlux += currentLiOHReactionHeatFlux;
            nutritionFoodReport = foodReport;
            nutritionWaterReport = waterReport;

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
            partTempDisplay = (float)KToC(part.temperature);
            cabinPartCouplingDisplay = (float)debugCabinPartConductance;
            cabinPartFlux = (float)(-debugCabinToPartKW);
            onboardHeatFlux = (float)(
                debugAvionicsHeat +
                currentSystemHeatFlux +
                currentLiOHReactionHeatFlux +
                debugCrewHeat +
                debugSystemHeat);
            activeControlFlux =
                heaterOutput -
                evaporatorOutput -
                airCoolingOutput -
                heatPumpOutput;
            effectiveFlux =
                cabinPartFlux +
                onboardHeatFlux +
                activeControlFlux;
            RefreshTemperatureReport();
            UpdateDBSTemperatureControlECRate();
            LogThermalDebug();
        }

        void RefreshCapabilityControls()
        {
            Fields["climateControlEnabled"].guiName = "Master Switch";
            Fields["heaterEnabled"].guiName = "Heater";
            Fields["evaporatorEnabled"].guiName = "Evaporator";
            Fields["airCoolingEnabled"].guiName = "Air Cooling";
            Fields["heatPumpEnabled"].guiName = "Heat Pump";
            Fields["cabinTemp"].guiName =
                cabinTemp < configuredMinSafeCabinTemp ||
                cabinTemp > configuredMaxSafeCabinTemp
                    ? KickUIFormat.Bad("Cabin Temperature")
                    : cabinTemp < configuredMinWarningCabinTemp ||
                      cabinTemp > configuredMaxWarningCabinTemp
                        ? KickUIFormat.Warning("Cabin Temperature")
                        : "Cabin Temperature";
            Fields["evaporatorOutput"].guiName = GetEvaporatorOutputLabel();
            Fields["airCoolingOutput"].guiName = GetAirCoolingOutputLabel();
            Fields["heatPumpOutput"].guiName = GetHeatPumpOutputLabel();
            Fields["climateControlEnabled"].guiActive = true;
            Fields["climateControlEnabled"].guiActiveEditor = true;
            Fields["climateControlStatus"].guiActive = true;
            Fields["enconECLimit"].guiActive = true;
            Fields["enconECLimit"].guiActiveEditor = true;
            Fields["thermostatTemp"].guiActive = true;
            Fields["thermostatTemp"].guiActiveEditor = true;
            Fields["cabinTemp"].guiActive = true;
            Fields["partTempDisplay"].guiActive = true;
            Fields["cabinPartFlux"].guiActive = true;
            Fields["onboardHeatFlux"].guiActive = true;
            Fields["activeControlFlux"].guiActive = true;
            Fields["effectiveFlux"].guiActive = true;
            Fields["heaterEnabled"].guiActive = true;
            Fields["heaterEnabled"].guiActiveEditor = true;
            Fields["heaterOutput"].guiActive = heaterOutput > 0.0005f;
            Fields["evaporatorEnabled"].guiActive = waterEvaporatorAvailable;
            Fields["evaporatorEnabled"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["evaporatorFeedMode"].guiActive = waterEvaporatorAvailable;
            Fields["evaporatorFeedMode"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["heatPumpEnabled"].guiActive = heatPumpAvailable;
            Fields["heatPumpEnabled"].guiActiveEditor = heatPumpAvailable;
            Fields["heatPumpOutput"].guiActive = heatPumpAvailable;
            Fields["waterEvaporatorWaterLimit"].guiActive = waterEvaporatorAvailable;
            Fields["waterEvaporatorWaterLimit"].guiActiveEditor = waterEvaporatorAvailable;
            Fields["evaporatorOutput"].guiActive = waterEvaporatorAvailable;
            Fields["airCoolingEnabled"].guiActive = airCoolingAvailable;
            Fields["airCoolingEnabled"].guiActiveEditor = airCoolingAvailable;
            Fields["airCoolingOutput"].guiActive = airCoolingAvailable;

            bool hasCrew = part != null && part.protoModuleCrew.Count > 0;
            Fields["nutritionFoodReport"].guiActive =
                lifeSupportEnabled && hasCrew;
            Fields["nutritionWaterReport"].guiActive =
                lifeSupportEnabled && hasCrew;
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
            if (waterEvaporatorStatus == "Atmosphere") return KickUIFormat.Bad("Heat Removed (Blocked by Atmosphere)");
            if (waterEvaporatorStatus == "No Water") return KickUIFormat.Bad("Heat Removed (No Water)");
            if (waterEvaporatorStatus == "No Power") return KickUIFormat.Bad("Heat Removed (No Power)");
            if (waterEvaporatorStatus == "EC Limit") return KickUIFormat.Warning("Heat Removed (No EC Budget)");
            if (waterEvaporatorStatus == "Water Limit") return KickUIFormat.Warning("Heat Removed (At Water Limit)");
            if (evaporatorWaterLimited) return KickUIFormat.Warning("Heat Removed (At Water Limit)");
            if (evaporatorResourceLimited) return KickUIFormat.Warning("Heat Removed (Water Limited)");
            if (evaporatorEnvironmentLimited) return KickUIFormat.Warning("Heat Removed (Atmosphere Limited)");
            if (evaporatorAtSetLimit) return KickUIFormat.Warning("Heat Removed (At EC Limit)");
            if (evaporatorEquipmentLimited) return KickUIFormat.Warning("Heat Removed (At System Capacity)");
            return "Heat Removed";
        }

        string GetAirCoolingOutputLabel()
        {
            if (airCoolingStatus == "No Atmosphere") return KickUIFormat.Bad("Heat Removed (No Atmosphere)");
            if (airCoolingStatus == "No Power") return KickUIFormat.Bad("Heat Removed (No Power)");
            if (airCoolingStatus == "EC Limit") return KickUIFormat.Warning("Heat Removed (No EC Budget)");
            if (airCoolingEnvironmentLimited) return KickUIFormat.Warning("Heat Removed (Thin Atmosphere)");
            if (airCoolingAtSetLimit) return KickUIFormat.Warning("Heat Removed (At EC Limit)");
            if (airCoolingEquipmentLimited) return KickUIFormat.Warning("Heat Removed (At System Capacity)");
            return "Heat Removed";
        }

        string GetHeatPumpOutputLabel()
        {
            if (heatPumpStatus == "No SystemHeat") return KickUIFormat.Bad("Heat Removed (System Unavailable)");
            if (heatPumpStatus == "No Loop") return KickUIFormat.Bad("Heat Removed (No Loop)");
            if (heatPumpStatus == "No Radiator") return KickUIFormat.Bad("Heat Removed (No Radiator)");
            if (heatPumpStatus == "Loop Hot") return KickUIFormat.Bad("Heat Removed (Loop Too Hot)");
            if (heatPumpStatus == "No Power") return KickUIFormat.Bad("Heat Removed (No Power)");
            if (heatPumpStatus == "EC Limit") return KickUIFormat.Warning("Heat Removed (No EC Budget)");
            if (heatPumpEnvironmentLimited) return KickUIFormat.Warning("Heat Removed (Loop Limited)");
            if (heatPumpAtSetLimit) return KickUIFormat.Warning("Heat Removed (At EC Limit)");
            if (heatPumpEquipmentLimited) return KickUIFormat.Warning("Heat Removed (At System Capacity)");
            return "Heat Removed";
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
            double budget = Math.Min(
                Math.Max(enconECLimit, 0),
                Math.Max(enconMaxECRate, 0));
            double controlRate = Math.Max(systemECRate, 0);
            if (budget + 0.000001 < controlRate) return 0;

            double equipmentBudget = Math.Max(budget - controlRate, 0);
            double heaterRate = heaterEnabled
                ? Math.Max(heaterMaxECRate, 0)
                : 0;
            double coolingRate = 0;
            if (waterEvaporatorAvailable && evaporatorEnabled)
                coolingRate = Math.Max(waterEvaporatorMaxECRate, 0);
            else if (airCoolingAvailable && airCoolingEnabled)
                coolingRate = Math.Max(airCoolingMaxECRate, 0);
            else if (heatPumpAvailable &&
                     heatPumpEnabled &&
                     IsSystemHeatLoopAvailable(GetSystemHeatModule()))
                coolingRate = Math.Max(heatPumpMaxECRate, 0);

            return (float)(
                controlRate +
                Math.Min(equipmentBudget, Math.Max(heaterRate, coolingRate)));
        }

        float GetEditorSystemHeatFluxEstimate()
        {
            if (!heatPumpAvailable || !heatPumpEnabled) return 0;

            double budget = Math.Min(
                Math.Max(enconECLimit, 0),
                Math.Max(enconMaxECRate, 0));
            double controlRate = Math.Max(systemECRate, 0);
            if (!climateControlEnabled || budget < controlRate) return 0;

            ModuleSystemHeat heatModule = GetSystemHeatModule();
            if (!IsSystemHeatLoopAvailable(heatModule)) return 0;

            double heatPumpRate = Math.Min(
                Math.Max(budget - controlRate, 0),
                Math.Max(heatPumpMaxECRate, 0));
            double loopTemperature = GetSystemHeatLoopTemperature(heatModule);
            double loopFactor = loopTemperature > 0
                ? GetHeatPumpLoopFactor(loopTemperature)
                : 1;
            double heatMovedKW =
                heatPumpRate * Math.Max(heatPumpHeatPerEC, 0) * loopFactor;
            double wasteKW = heatPumpRate;
            return (float)(heatMovedKW + wasteKW);
        }

        void UpdateEditorWaterEvaporatorEstimate()
        {
            if (!HighLogic.LoadedSceneIsEditor || !waterEvaporatorAvailable) return;

            waterEvaporatorWaterRate =
                Math.Min(
                    Math.Max(waterEvaporatorWaterLimit, 0),
                    Math.Max(waterEvaporatorMaxWaterRate, 0));
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
            if (cabinTemp < configuredMinSafeCabinTemp)
            {
                double remaining = status != null ? scenario.GraceTemp - status.tempRangeTime : scenario.GraceTemp;
                climateControlStatus = KickUIFormat.ReportLine(KickUIFormat.Bad($"Too Cold ({KickUIFormat.Timer(remaining)})"));
            }
            else if (cabinTemp > configuredMaxSafeCabinTemp)
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
            if (pressureKPa <= configuredWaterEvaporatorFullPressureKPa) return 1;
            if (pressureKPa >= configuredWaterEvaporatorMaxPressureKPa) return 0;

            return Clamp01(1.0 - ((pressureKPa - configuredWaterEvaporatorFullPressureKPa) /
                (configuredWaterEvaporatorMaxPressureKPa -
                    configuredWaterEvaporatorFullPressureKPa)));
        }

        double GetAirCoolingPressureFactor(double pressureKPa)
        {
            if (pressureKPa <= configuredAirCoolingMinPressureKPa) return 0;
            if (pressureKPa >= configuredAirCoolingFullPressureKPa) return 1;

            return Clamp01((pressureKPa - configuredAirCoolingMinPressureKPa) /
                (configuredAirCoolingFullPressureKPa -
                    configuredAirCoolingMinPressureKPa));
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
            systemHeatFluxEstimate = 0;
            double currentCabinTempK = CToK(cabinTemp);
            double dt = TimeWarp.fixedDeltaTime;
            double targetTempK = CToK(thermostatTemp);
            isWaterEvaporatorActive = false;
            isAirCoolingActive = false;
            waterEvaporatorWaterRate = 0;

            double totalECBudget = Math.Max(enconECLimit, 0);
            double controlECRate = Math.Max(systemECRate, 0);
            bool controlBudgetAvailable =
                totalECBudget + 0.000001 >= controlECRate;
            double equipmentECBudget = controlBudgetAvailable
                ? Math.Max(totalECBudget - controlECRate, 0)
                : 0;
            double remainingEquipmentECRate = equipmentECBudget;
            double systemEC = controlECRate * TimeWarp.fixedDeltaTime;
            bool environmentalControlPowered = false;

            if (TimeWarp.CurrentRate > configuredHighWarpStabilizationThreshold)
            {
                SetSystemHeatPumpFlux(0, 0, false);
                if (heatPumpAvailable && heatPumpEnabled && cabinTemp > thermostatTemp &&
                    !HasSystemHeatRadiator(GetSystemHeatModule()))
                {
                    heatPumpStatus = "No Radiator";
                    return;
                }

                if (climateControlEnabled && controlBudgetAvailable)
                {
                    if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
                    {
                        currentTemperatureControlECRate += Math.Max(systemECRate, 0);
                        bool hasEquipmentBudget =
                            remainingEquipmentECRate > 0.000001;
                        bool canHeat =
                            hasEquipmentBudget &&
                            heaterEnabled &&
                            heaterMaxECRate > 0 &&
                            cabinTemp < thermostatTemp;
                        bool canCool =
                            hasEquipmentBudget &&
                            ((waterEvaporatorAvailable &&
                              evaporatorEnabled &&
                              waterEvaporatorMaxECRate > 0 &&
                              waterEvaporatorWaterLimit > 0) ||
                             (airCoolingAvailable &&
                              airCoolingEnabled &&
                              airCoolingMaxECRate > 0) ||
                             (heatPumpAvailable &&
                              heatPumpEnabled &&
                              heatPumpMaxECRate > 0));
                        if (canHeat || (canCool && cabinTemp > thermostatTemp))
                        {
                            cabinTemp = thermostatTemp;
                        }
                    }
                }
                else if (climateControlEnabled)
                {
                    climateControlStatus = "EC Limit";
                }

                return;
            }

            if (climateControlEnabled)
            {
                if (!controlBudgetAvailable)
                {
                    climateControlStatus = "EC Limit";
                    heaterStatus = "See CC Status";
                    heatPumpStatus = "See CC Status";
                    waterEvaporatorStatus = "See CC Status";
                    airCoolingStatus = "See CC Status";
                }
                else if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
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
                debugCabinPartConductance =
                    Math.Max(unpressurizedCabinPartConductance, 0);
            }
            else if (lifeSupport != null &&
                     KickLifeSupportModule.UsesPartialPressurization(lifeSupport.atmosphereControlMode))
            {
                debugCabinPartConductance =
                    Math.Max(partiallyPressurizedCabinPartConductance, 0);
            }
            else
            {
                debugCabinPartConductance =
                    Math.Max(pressurizedCabinPartConductance, 0);
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

                double heaterECBudget = heaterEnabled
                    ? Math.Min(
                        remainingEquipmentECRate,
                        Math.Max(heaterMaxECRate, 0))
                    : 0;
                double heaterLimitKW =
                    heaterECBudget * Math.Max(heaterHeatPerEC, 0);
                double requestedHeaterKW = GetHeatingToTargetKW(projectedCabinTempK, targetTempK, cabinHeatCapacity, dt);
                double heaterKW = Math.Min(heaterLimitKW, requestedHeaterKW);

                if (heaterKW > 0.000001 && heaterLimitKW > 0)
                {
                    double heatPerEC = Math.Max(heaterHeatPerEC, 0.000001);
                    double heaterECRate = heaterKW / heatPerEC;
                    double heaterEC = heaterECRate * dt;
                    if (part.RequestResource(ecId, heaterEC) < heaterEC * 0.99)
                    {
                        heaterStatus = "No Power";
                    }
                    else
                    {
                        currentTemperatureControlECRate += heaterECRate;
                        remainingEquipmentECRate =
                            Math.Max(remainingEquipmentECRate - heaterECRate, 0);
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
                            double evaporatorECBudget = Math.Min(
                                remainingEquipmentECRate,
                                Math.Max(waterEvaporatorMaxECRate, 0));
                            double evaporatorHeatPerEC =
                                Math.Max(waterEvaporatorHeatPerEC, 0);
                            double evaporatorLimitKW =
                                evaporatorECBudget *
                                evaporatorHeatPerEC *
                                pressureFactor;
                            double evaporatorEfficiency =
                                Math.Max(waterEvaporatorEfficiency, 0.000001);
                            double requestedGrossCoolingKW =
                                requestedActiveCoolingKW / evaporatorEfficiency;
                            double desiredGrossCoolingKW = Math.Min(evaporatorLimitKW, requestedGrossCoolingKW);
                            evaporatorEnvironmentLimited = pressureFactor < 0.999;
                            bool evaporatorDemandLimited =
                                pressureFactor >= 0.999 &&
                                requestedGrossCoolingKW >
                                    evaporatorLimitKW + 0.000001;
                            evaporatorAtSetLimit =
                                evaporatorDemandLimited &&
                                remainingEquipmentECRate + 0.000001 <
                                    Math.Max(waterEvaporatorMaxECRate, 0);
                            evaporatorEquipmentLimited =
                                evaporatorDemandLimited &&
                                !evaporatorAtSetLimit;

                            if (desiredGrossCoolingKW > 0.000001 &&
                                evaporatorECBudget > 0 &&
                                evaporatorHeatPerEC > 0)
                            {
                                double requestedEnergyKJ = desiredGrossCoolingKW * dt;
                                double waterBudget =
                                    Math.Max(waterEvaporatorWaterLimit, 0) * dt;
                                double remainingWaterBudget = waterBudget;
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
                                    configuredEvaporationEnergyKJPerUnit *
                                    configuredWasteWaterCoolingFactor;
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
                                        remainingWaterBudget,
                                        Math.Min(
                                            availableWater,
                                            remainingEnergyKJ /
                                            configuredEvaporationEnergyKJPerUnit));
                                    remainingEnergyKJ -=
                                        waterToConsume * configuredEvaporationEnergyKJPerUnit;
                                    remainingWaterBudget -= waterToConsume;
                                }

                                if (allowWasteWater && wasteWaterEnergyKJPerUnit > 0)
                                {
                                    wasteWaterToConsume = Math.Min(
                                        remainingWaterBudget,
                                        Math.Min(
                                            usableWasteWater,
                                            remainingEnergyKJ / wasteWaterEnergyKJPerUnit));
                                    remainingEnergyKJ -= wasteWaterToConsume * wasteWaterEnergyKJPerUnit;
                                    remainingWaterBudget -= wasteWaterToConsume;
                                }

                                if (!freshWaterFirst && allowFreshWater)
                                {
                                    waterToConsume = Math.Min(
                                        remainingWaterBudget,
                                        Math.Min(
                                            availableWater,
                                            Math.Max(remainingEnergyKJ, 0) /
                                            configuredEvaporationEnergyKJPerUnit));
                                    remainingEnergyKJ -=
                                        waterToConsume *
                                        configuredEvaporationEnergyKJPerUnit;
                                    remainingWaterBudget -= waterToConsume;
                                }

                                double wasteWaterEnergyKJ = wasteWaterToConsume * wasteWaterEnergyKJPerUnit;
                                double supportedEnergyKJ =
                                    wasteWaterEnergyKJ +
                                    (waterToConsume * configuredEvaporationEnergyKJPerUnit);
                                double potentialGrossCoolingKW = supportedEnergyKJ / dt;
                                bool needsMoreWater =
                                    supportedEnergyKJ < requestedEnergyKJ * 0.999;
                                double plannedWater =
                                    wasteWaterToConsume + waterToConsume;
                                evaporatorWaterLimited =
                                    needsMoreWater &&
                                    (waterBudget <= 0 ||
                                     plannedWater >= waterBudget * 0.999);
                                evaporatorResourceLimited =
                                    needsMoreWater && !evaporatorWaterLimited;

                                if (potentialGrossCoolingKW <= 0.000001)
                                {
                                    waterEvaporatorStatus =
                                        evaporatorWaterLimited
                                            ? "Water Limit"
                                            : "No Water";
                                }
                                else
                                {
                                    double evaporatorECPerSecond =
                                        potentialGrossCoolingKW /
                                        Math.Max(
                                            evaporatorHeatPerEC * pressureFactor,
                                            0.000001);
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
                                            (consumedWater *
                                                configuredEvaporationEnergyKJPerUnit);
                                        double actualGrossCoolingKW = actualEnergyKJ / dt;
                                        double actualCoolingKW =
                                            actualGrossCoolingKW * evaporatorEfficiency;

                                        if (actualCoolingKW <= 0.000001)
                                        {
                                            waterEvaporatorStatus = "No Water";
                                        }
                                        else
                                        {
                                            currentTemperatureControlECRate += evaporatorECPerSecond;
                                            remainingEquipmentECRate = Math.Max(
                                                remainingEquipmentECRate -
                                                evaporatorECPerSecond,
                                                0);
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
                            else if (evaporatorECBudget <= 0 ||
                                     evaporatorHeatPerEC <= 0)
                            {
                                waterEvaporatorStatus = "EC Limit";
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
                            double airCoolingECBudget = Math.Min(
                                remainingEquipmentECRate,
                                Math.Max(airCoolingMaxECRate, 0));
                            double airCoolingPerformance =
                                Math.Max(airCoolingHeatPerEC, 0) *
                                pressureFactor;
                            double coolingLimitKW =
                                airCoolingECBudget * airCoolingPerformance;
                            double coolingKW = Math.Min(coolingLimitKW, requestedActiveCoolingKW);
                            airCoolingEnvironmentLimited = pressureFactor < 0.999;
                            bool airCoolingDemandLimited =
                                pressureFactor >= 0.999 &&
                                requestedActiveCoolingKW >
                                    coolingLimitKW + 0.000001;
                            airCoolingAtSetLimit =
                                airCoolingDemandLimited &&
                                remainingEquipmentECRate + 0.000001 <
                                    Math.Max(airCoolingMaxECRate, 0);
                            airCoolingEquipmentLimited =
                                airCoolingDemandLimited &&
                                !airCoolingAtSetLimit;

                            if (coolingKW > 0.000001 &&
                                airCoolingECBudget > 0 &&
                                airCoolingPerformance > 0)
                            {
                                double airCoolingECPerSecond =
                                    coolingKW / airCoolingPerformance;
                                double requestedEC =
                                    airCoolingECPerSecond * dt;
                                double consumedEC = requestedEC > 0 ? part.RequestResource(ecId, requestedEC) : 0;

                                if (requestedEC > 0 && consumedEC < requestedEC * 0.99)
                                {
                                    airCoolingStatus = "No Power";
                                }
                                else
                                {
                                    currentTemperatureControlECRate +=
                                        airCoolingECPerSecond;
                                    remainingEquipmentECRate = Math.Max(
                                        remainingEquipmentECRate -
                                        airCoolingECPerSecond,
                                        0);
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
                            else if (airCoolingECBudget <= 0)
                            {
                                airCoolingStatus = "EC Limit";
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
                                double heatPumpECBudget = Math.Min(
                                    remainingEquipmentECRate,
                                    Math.Max(heatPumpMaxECRate, 0));
                                double heatPumpPerformance =
                                    Math.Max(heatPumpHeatPerEC, 0) *
                                    loopFactor;
                                double heatPumpLimitKW =
                                    heatPumpECBudget * heatPumpPerformance;
                                double transferKW = Math.Min(heatPumpLimitKW, requestedActiveCoolingKW);
                                heatPumpEnvironmentLimited = loopFactor < 0.999;
                                bool heatPumpDemandLimited =
                                    loopFactor >= 0.999 &&
                                    requestedActiveCoolingKW >
                                        heatPumpLimitKW + 0.000001;
                                heatPumpAtSetLimit =
                                    heatPumpDemandLimited &&
                                    remainingEquipmentECRate + 0.000001 <
                                        Math.Max(heatPumpMaxECRate, 0);
                                heatPumpEquipmentLimited =
                                    heatPumpDemandLimited &&
                                    !heatPumpAtSetLimit;

                                if (transferKW > 0.000001 &&
                                    heatPumpECBudget > 0 &&
                                    heatPumpPerformance > 0)
                                {
                                    double heatPumpECPerSecond =
                                        transferKW / heatPumpPerformance;
                                    double heatPumpEC =
                                        heatPumpECPerSecond * dt;
                                    if (part.RequestResource(ecId, heatPumpEC) < heatPumpEC * 0.99)
                                    {
                                        heatPumpStatus = "No Power";
                                        SetSystemHeatPumpFlux(0, 0, false);
                                    }
                                    else
                                    {
                                        double wasteHeatKW =
                                            heatPumpECPerSecond;
                                        if (SetSystemHeatPumpFlux(projectedCabinTempK, transferKW + wasteHeatKW, true))
                                        {
                                            currentTemperatureControlECRate +=
                                                heatPumpECPerSecond;
                                            remainingEquipmentECRate = Math.Max(
                                                remainingEquipmentECRate -
                                                heatPumpECPerSecond,
                                                0);
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
                                    heatPumpStatus =
                                        heatPumpECBudget <= 0
                                            ? "EC Limit"
                                            : "Standby";
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

        }

        double GetCabinHeatCapacity()
        {
            double airMass =
                part.CrewCapacity * Math.Max(airVolumePerSeat, 0) *
                configuredAirDensity;
            float baseMass = part.partInfo != null && part.partInfo.partPrefab != null
                ? part.partInfo.partPrefab.mass
                : part.mass;
            float moduleMass = part.GetModuleMass(baseMass, ModifierStagingSituation.CURRENT);
            double partMassKg = Math.Max(baseMass + moduleMass, 0) * 1000.0;
            return (airMass * configuredAirSpecificHeat) +
                (partMassKg * Math.Max(cabinMassFraction, 0) *
                    configuredGenericCabinSpecificHeat);
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
                $"onboardHeat={onboardHeatFlux:F3}kW activeControl={activeControlFlux:F3}kW effective={effectiveFlux:F3}kW partHeat={debugPartHeat:F3}kW cabinPart={cabinPartFlux:F3}kW cabinToPart={debugCabinToPartKW:F4}kW stockFlux={debugStockPartFluxKW:F4}kW " +
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

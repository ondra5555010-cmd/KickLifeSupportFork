using System;
using UnityEngine;

namespace KickLifeSupport
{
    public class KickTemperatureControlModule : PartModule, IAnalyticTemperatureModifier
    {
        KickLifeSupportSettings gameSettings;

        #region Thermal GUI Fields
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Environmental Control", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool climateControlEnabled = true;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Auto-deploy Radiators", groupName = "KICKTEMP", groupDisplayName = "Environmental Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool autoDeployRadiators = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Situation Report", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        public string climateControlStatus = "On";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Thermostat Setting", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        [UI_FloatRange(minValue = 10f, maxValue = 30f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float thermostatTemp = 22f;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Cabin Temp", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F1", guiUnits = "C")]
        public float cabinTemp = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Cabin Heater", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        public string heaterStatus = "Off";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Auto Radiators", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        public string radiatorStatus = "Off";

        [KSPField(guiActive = true, guiActiveEditor = true, guiName = "Heater Strength", groupName = "KICKTEMP", groupDisplayName = "Environmental Control")]
        [UI_FloatRange(minValue = 0f, maxValue = 40f, stepIncrement = 0.5f, scene = UI_Scene.All)]
        public float heaterHeat = 0.5f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Active Flux", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float heatFlux = 0;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Passive Flux", groupName = "KICKTEMP", groupDisplayName = "Environmental Control", guiFormat = "F3", guiUnits = " kW")]
        public float passiveFlux = 0;

        [KSPField]
        public float dbsTemperatureControlECRate = 0f;
        #endregion

        #region Thermal Rates
        [KSPField] public float systemECRate = 0.03f;
        [KSPField] public float systemHeat = 0.03f;
        #endregion

        #region Thermal Constants
        internal const double thermostatDeadband = 2;
        internal const double airSpecificHeat = 1005.0;
        internal const double airDensity = 0.001225;
        internal const double wallCoupling = 0.003;
        #endregion

        public bool isHeaterActive = false;
        public bool isAutoRadiatorActive = false;
        bool isHeaterConsumingEC = false;
        bool canAutoRadiator = false;
        int ecId = -1;
        double cachedAnalyticTemp;

        public override void OnStart(StartState state)
        {
            gameSettings = HighLogic.CurrentGame.Parameters.CustomParams<KickLifeSupportSettings>();

            PartResourceDefinition ecDef = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (ecDef != null) ecId = ecDef.id;

            if (HighLogic.LoadedSceneIsFlight && (cabinTemp == 0 || cabinTemp < -200))
            {
                cabinTemp = (float)KToC(part.temperature);
            }

            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        public override void OnUpdate()
        {
            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            RefreshCapabilityControls();
            UpdateDBSTemperatureControlECRate();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel.loaded) return;

            if (cabinTemp == 0 || cabinTemp < -200)
            {
                cabinTemp = (float)KToC(part.temperature);
            }

            double totalFlux = 0;

            KickAvionicsModule avionics = part.FindModuleImplementing<KickAvionicsModule>();
            if (avionics != null)
            {
                totalFlux += avionics.currentHeatFlux;
            }

            KickLifeSupportModule lifeSupport = part.FindModuleImplementing<KickLifeSupportModule>();
            if (lifeSupport != null)
            {
                totalFlux += lifeSupport.currentHeatFlux;
            }

            int crewCount = part.protoModuleCrew.Count;
            if (crewCount > 0 && KickLifeSupportScenario.Instance != null)
            {
                totalFlux += crewCount * KickLifeSupportScenario.Instance.kerbalHeat;
            }

            if (gameSettings != null && gameSettings.useCabinTempSystem)
            {
                RunThermalLogic(ref totalFlux);

                double airToHullFlux = (cabinTemp - KToC(part.temperature)) * wallCoupling;
                part.AddThermalFlux(airToHullFlux);
            }
            else
            {
                totalFlux = 0;
            }

            heatFlux = (float)totalFlux;
            RefreshTemperatureReport();
            UpdateDBSTemperatureControlECRate();
        }

        void RefreshCapabilityControls()
        {
            Fields["climateControlEnabled"].guiActive = true;
            Fields["climateControlEnabled"].guiActiveEditor = true;
            Fields["autoDeployRadiators"].guiActive = true;
            Fields["autoDeployRadiators"].guiActiveEditor = true;
            Fields["climateControlStatus"].guiActive = true;
            Fields["thermostatTemp"].guiActive = true;
            Fields["thermostatTemp"].guiActiveEditor = true;
            Fields["cabinTemp"].guiActive = true;
            Fields["heaterStatus"].guiActive = true;
            Fields["radiatorStatus"].guiActive = true;
            Fields["heaterHeat"].guiActive = true;
            Fields["heaterHeat"].guiActiveEditor = true;
            Fields["heatFlux"].guiActive = true;
            Fields["passiveFlux"].guiActive = true;
        }

        void UpdateDBSTemperatureControlECRate()
        {
            float estimate = 0f;
            if (climateControlEnabled && (gameSettings == null || gameSettings.useCabinTempSystem))
            {
                estimate += systemECRate;

                if (HighLogic.LoadedSceneIsEditor || isHeaterConsumingEC)
                {
                    estimate += heaterHeat;
                }
            }

            dbsTemperatureControlECRate = estimate;
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

        void ActivateRadiators()
        {
            foreach (Part p in vessel.parts)
            {
                KickRadiatorControlModule lsRadiator = p.FindModuleImplementing<KickRadiatorControlModule>();
                if (lsRadiator == null || !lsRadiator.allowAutoDeploy) continue;

                ModuleActiveRadiator activeRadiator = p.FindModuleImplementing<ModuleActiveRadiator>();
                if (activeRadiator != null)
                {
                    activeRadiator.IsCooling = true;
                }

                ModuleDeployableRadiator deployableRadiator = p.FindModuleImplementing<ModuleDeployableRadiator>();
                if (deployableRadiator != null)
                {
                    deployableRadiator.Extend();
                }
            }
        }

        void DeactiveRadiators()
        {
            foreach (Part p in vessel.parts)
            {
                KickRadiatorControlModule lsRadiator = p.FindModuleImplementing<KickRadiatorControlModule>();
                if (lsRadiator == null || !lsRadiator.allowAutoDeploy) continue;

                ModuleActiveRadiator activeRadiator = p.FindModuleImplementing<ModuleActiveRadiator>();
                if (activeRadiator != null)
                {
                    activeRadiator.IsCooling = false;
                }

                ModuleDeployableRadiator deployableRadiator = p.FindModuleImplementing<ModuleDeployableRadiator>();
                if (deployableRadiator != null)
                {
                    deployableRadiator.Retract();
                }
            }
        }

        void RunThermalLogic(ref double totalFlux)
        {
            isHeaterConsumingEC = false;
            double currentAirTempK = CToK(cabinTemp);
            double dt = TimeWarp.fixedDeltaTime;

            double hullTemp = part.temperature;
            double passiveChange = (hullTemp - currentAirTempK) * wallCoupling * dt;
            currentAirTempK += passiveChange;

            double systemEC = systemECRate * TimeWarp.fixedDeltaTime;
            double heaterEC = heaterHeat * TimeWarp.fixedDeltaTime;

            if (TimeWarp.CurrentRate > 100f)
            {
                if (climateControlEnabled)
                {
                    if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
                    {
                        cabinTemp = thermostatTemp;
                    }
                }

                return;
            }

            if (climateControlEnabled)
            {
                if (part.RequestResource(ecId, systemEC) >= systemEC * 0.99)
                {
                    totalFlux += systemHeat;
                    climateControlStatus = "On";

                    float currentAirC = (float)KToC(currentAirTempK);

                    float lowThreshold = (float)(thermostatTemp - (thermostatDeadband / 2));
                    float highThreshold = (float)(thermostatTemp + (thermostatDeadband / 2));
                    float excessiveThreshold = (float)(thermostatTemp + (thermostatDeadband * 4));

                    if (currentAirC < lowThreshold)
                    {
                        heaterStatus = "Active";
                        isHeaterActive = true;
                        canAutoRadiator = false;
                    }
                    else if (currentAirC > excessiveThreshold)
                    {
                        heaterStatus = "Standby";
                        isHeaterActive = false;
                        canAutoRadiator = true;
                    }
                    else if (currentAirC > highThreshold)
                    {
                        heaterStatus = "Standby";
                        isHeaterActive = false;
                        canAutoRadiator = false;
                    }

                    if (isHeaterActive)
                    {
                        if (part.RequestResource(ecId, heaterEC) < heaterEC * 0.99)
                        {
                            heaterStatus = "No Power";
                        }
                        else
                        {
                            heaterStatus = "Active";
                            isHeaterConsumingEC = true;
                            totalFlux += heaterHeat;
                        }
                    }

                    if (autoDeployRadiators)
                    {
                        if (canAutoRadiator)
                        {
                            ActivateRadiators();
                            radiatorStatus = "Active";
                        }
                        else
                        {
                            DeactiveRadiators();
                            radiatorStatus = "Standby";
                        }
                    }
                }
                else
                {
                    climateControlStatus = "No Power";
                    heaterStatus = "See CC Status";
                    radiatorStatus = "See CC Status";
                    isHeaterActive = false;
                    isHeaterConsumingEC = false;
                }
            }
            else
            {
                climateControlStatus = "Disabled";
                heaterStatus = "Disabled";
                radiatorStatus = "Disabled";
                isHeaterActive = false;
                isHeaterConsumingEC = false;
            }

            double activeChange = 0;
            double airMass = part.CrewCapacity * KickLifeSupportModule.airPerSeat * airDensity;
            if (airMass < 1.0) airMass = 5.0;

            if (Math.Abs(totalFlux) > 0.00001)
            {
                double energyJoules = (totalFlux * 1000.0) * dt;
                activeChange = energyJoules / (airMass * airSpecificHeat);
                currentAirTempK += activeChange;
            }

            cabinTemp = (float)KToC(currentAirTempK);

            double passiveEnergyJ = passiveChange * (airMass * airSpecificHeat);
            double passiveKW = (passiveEnergyJ / dt) / 1000.0;
            passiveFlux = (float)passiveKW;
        }

        public void SetAnalyticTemperature(FlightIntegrator fi, double analyticTemp, double toBeInternal, double toBeSkin)
        {
            cachedAnalyticTemp = toBeInternal;
        }

        public double GetSkinTemperature(out bool lerp)
        {
            lerp = false;
            return -1;
        }

        public double GetInternalTemperature(out bool lerp)
        {
            lerp = false;

            if (climateControlEnabled)
            {
                double targetK = CToK(thermostatTemp);

                if (cachedAnalyticTemp > targetK)
                    cabinTemp = (float)KToC(cachedAnalyticTemp);
                else
                    cabinTemp = (float)KToC(targetK);
            }
            else
            {
                cabinTemp = (float)KToC(cachedAnalyticTemp);
            }

            return -1;
        }
    }
}

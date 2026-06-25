using UnityEngine;

namespace KickLifeSupport
{
    public partial class KickLifeSupportModule : PartModule
    {
        private const float UpdateInterval = 5f;

        #region Persistent Fields
        [KSPField(isPersistant = true)]
        public float lowO2Time = 0f;
        [KSPField(isPersistant = true)]
        public float lowWaterTime = 0f;
        [KSPField(isPersistant = true)]
        public float lowFoodTime = 0f;
        [KSPField(isPersistant = true)]
        public float cabinCO2 = 0f;
        #endregion

        #region Resource IDs
        int wasteId = -1;
        int liohId = -1;
        int ecId = -1;
        int o2Id = -1;
        #endregion

        /// <summary>
        /// The amount of air (in liters) available per kerbal.
        /// </summary>
        internal const double airPerSeat = 2000;

        public const int AtmosphereControlNone = 0;
        public const int AtmosphereControlPressurizedCabin = 1;
        public const int AtmosphereControlOpenLoopELS = 2;
        public const int AtmosphereControlLiOH = 3;
        public const int AtmosphereControlRegenerativeScrubber = 4;
        public const int AtmosphereControlSolidAmine = 5;

        internal const double liohReactionHeatPerUnit = 4.0;

        #region Module Fields
        [KSPField]
        public bool lifeSupportEnabled = true;

        [KSPField(isPersistant = true)]
        public int atmosphereControlMode = AtmosphereControlNone;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Installed System", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string installedAtmosphereControl = "Unpressurized Cabin";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Situation Report", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string lsStatus = "Nominal";

        //[KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Cabin Pressure", groupName = "KICKLS", groupDisplayName = "Life Support", guiFormat = "F1", guiUnits = " kPa")]
        public float cabinPressure = 101.325f;  // pressure in kPa
        // TODO: Implement cabin pressure simulation

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = false, guiName = "Atmospheric Control", groupName = "KICKATM", groupDisplayName = "Atmospheric Control"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool scrubberEnabled = true;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "System Report", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string scrubberStatus = "On";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Level", groupName = "KICKATM", groupDisplayName = "Atmospheric Control", guiFormat = "P1")]
        public float co2Level = 0f;

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "CO2 Warning", groupName = "KICKATM", groupDisplayName = "Atmospheric Control")]
        public string co2WarningReport = "";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Food", groupName = "KICKNUTRITION", groupDisplayName = "Nutrition")]
        public string foodReport = "Available";

        [KSPField(guiActive = true, guiActiveEditor = false, guiName = "Water", groupName = "KICKNUTRITION", groupDisplayName = "Nutrition")]
        public string waterReport = "Available";

        [KSPField]
        public float dbsLifeSupportECRate = 0f;
        #endregion

        public double currentHeatFlux = 0;
        public string rawScrubberStatus = "On";

        bool partActionWindowInitialized = false;
        int lastAtmosphereControlMode = -1;
        float scrubberECRequestRate = 0.05f;
        float regenerativeScrubberECRequestRate = 0.5f;
        float openLoopELSECRequestRate = 0.005f;
        float openLoopELSHeatPerEC = 1.0f;

        public override void OnStart(StartState state)
        {
            LoadDBSRates();

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
        }

        public override void OnUpdate()
        {
            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            RefreshScrubberControls();
            UpdateDBSLifeSupportECRate();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || !vessel.loaded) return;

            LifeSupportStatus data = null;
            if (lifeSupportEnabled && KickLifeSupportScenario.Instance != null)
                data = KickLifeSupportScenario.Instance.GetData(vessel.id);
            else if (lifeSupportEnabled)
                return;

            double dt = TimeWarp.fixedDeltaTime;
            currentHeatFlux = 0;

            UpdateDBSLifeSupportECRate();

            if (lifeSupportEnabled)
            {
                // Scrubber
                if (atmosphereControlMode == AtmosphereControlNone)
                {
                    SetAtmosphericControlStatus(GetAmbientAtmosphereStatus(vessel));
                }
                else if (IsRegenerativeScrubber())
                {
                    if (data.activeRegenerativeScrubberSystemCapacity > 0)
                    {
                        currentHeatFlux += GetScaledSystemHeat(regenerativeScrubberECRequestRate, data.activeRegenerativeScrubberSystemCapacity);
                    }
                }
                else if (atmosphereControlMode == AtmosphereControlLiOH)
                {
                    if (data.activeLiOHSystemCapacity > 0)
                    {
                        currentHeatFlux += GetScaledSystemHeat(scrubberECRequestRate, data.activeLiOHSystemCapacity);
                    }

                    if (data.lastLiOHScrubAmount > 0 && data.activeLiOHScrubberCount > 0 && dt > 0)
                    {
                        currentHeatFlux += (data.lastLiOHScrubAmount / data.activeLiOHScrubberCount / dt) * liohReactionHeatPerUnit;
                    }
                }
                else if (atmosphereControlMode == AtmosphereControlOpenLoopELS && rawScrubberStatus == "ELS Active")
                {
                    currentHeatFlux += GetScaledECRequestEstimate(openLoopELSECRequestRate) * openLoopELSHeatPerEC;
                }
            }

            if (lifeSupportEnabled)
            {
                cabinCO2 = data.cabinCO2;
                int cabinCapacity = GetVesselCabinAtmosphereCapacity(vessel);
                co2Level = cabinCapacity > 0 ? (float)(cabinCO2 / (cabinCapacity * airPerSeat)) : 0f;
                if (float.IsNaN(co2Level) || float.IsInfinity(co2Level)) co2Level = 0f;
                RefreshStatusReports(data, co2Level, part.protoModuleCrew.Count);
            }

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
                Fields["scrubberEnabled"].guiActive = hasScrubber;
                Fields["scrubberStatus"].guiActive = hasScrubber;
                Fields["foodReport"].guiActive = lifeSupportEnabled;
                Fields["waterReport"].guiActive = lifeSupportEnabled;
                Events["ReloadScrubber"].active = lifeSupportEnabled && usesLiOH;
                Events["ReloadScrubber"].guiActive = lifeSupportEnabled && usesLiOH;

                if (!usesLiOH)
                {
                    SetLiOHResource(false);
                }
            }

            installedAtmosphereControl = GetAtmosphericControlDisplayName();
        }

        string GetAtmosphericControlDisplayName()
        {
            switch (atmosphereControlMode)
            {
                case AtmosphereControlOpenLoopELS:
                    return "Open-Loop Venting";
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
            return atmosphereControlMode == AtmosphereControlOpenLoopELS ||
                   atmosphereControlMode == AtmosphereControlLiOH ||
                   IsRegenerativeScrubber();
        }

        bool IsRegenerativeScrubber()
        {
            return atmosphereControlMode == AtmosphereControlRegenerativeScrubber ||
                   atmosphereControlMode == AtmosphereControlSolidAmine;
        }

        void RefreshStatusReports(LifeSupportStatus data, double currentCO2Level, int crewCount)
        {
            bool hasCrew = crewCount > 0;
            Fields["lsStatus"].guiActive = lifeSupportEnabled && hasCrew;
            Fields["co2Level"].guiActive = lifeSupportEnabled && atmosphereControlMode != AtmosphereControlNone;
            Fields["co2WarningReport"].guiActive = lifeSupportEnabled && atmosphereControlMode != AtmosphereControlNone;
            Fields["foodReport"].guiActive = lifeSupportEnabled && hasCrew;
            Fields["waterReport"].guiActive = lifeSupportEnabled && hasCrew;

            if (!hasCrew) return;

            KickLifeSupportScenario scenario = KickLifeSupportScenario.Instance;
            double breathingRemaining = -1;
            bool ambientOnly = atmosphereControlMode == AtmosphereControlNone;

            if (scenario != null)
            {
                if (ambientOnly && data.ambientExposureTime > 0)
                {
                    breathingRemaining = scenario.GraceOxygen - data.ambientExposureTime;
                }
                else if (!ambientOnly && data.lowO2Time > 0)
                {
                    breathingRemaining = scenario.GraceOxygen - data.lowO2Time;
                }

            }

            if (breathingRemaining >= 0)
            {
                string label = ambientOnly ? "No Ambient Air" : "Unbreathable Atmosphere";
                lsStatus = KickUIFormat.ReportLine(KickUIFormat.Bad($"{label} ({KickUIFormat.Timer(breathingRemaining)})"));
            }
            else
            {
                lsStatus = KickUIFormat.ReportLine(KickUIFormat.Good(ambientOnly ? "Ambient Air Safe" : "Breathable Atmosphere"));
            }

            scrubberStatus = KickUIFormat.ReportLine(FormatAtmosphericControlStatus(rawScrubberStatus));
            co2WarningReport = FormatCO2Warning(currentCO2Level, data);

            if (scenario != null && data.lowFoodTime > 0)
            {
                foodReport = KickUIFormat.Bad($"Food Unavailable ({KickUIFormat.Timer(scenario.GraceFood - data.lowFoodTime)})");
            }
            else
            {
                foodReport = KickUIFormat.Good("Food Available");
            }

            if (scenario != null && data.lowWaterTime > 0)
            {
                waterReport = KickUIFormat.Bad($"Water Unavailable ({KickUIFormat.Timer(scenario.GraceWater - data.lowWaterTime)})");
            }
            else
            {
                waterReport = KickUIFormat.Good("Water Available");
            }
        }

        string FormatAtmosphericControlStatus(string rawStatus)
        {
            if (atmosphereControlMode == AtmosphereControlNone)
            {
                if (rawStatus == "Safe Env") return KickUIFormat.Good("Ambient Atmosphere");
                if (rawStatus == "No O2 Atmo") return KickUIFormat.Bad("Unbreathable Atmosphere");
                if (rawStatus == "Thin Atmo") return KickUIFormat.Bad("Atmosphere Too Thin");
                if (rawStatus == "Underwater") return KickUIFormat.Bad("Underwater");
                return KickUIFormat.Warning("Unbreathable Atmosphere");
            }

            if (atmosphereControlMode == AtmosphereControlOpenLoopELS)
            {
                if (rawStatus == "ELS Active") return KickUIFormat.Good("Active Venting");
                if (rawStatus == "Safe Env") return KickUIFormat.Good("Ambient Atmosphere");
                if (rawStatus == "Thin Atmo") return KickUIFormat.Bad("Atmosphere Too Thin");
                if (rawStatus == "Underwater") return KickUIFormat.Bad("Underwater");
                if (rawStatus == "No EC") return KickUIFormat.Bad("No Electricity");
                if (rawStatus == "No O2") return KickUIFormat.Bad("No Oxygen");
                return KickUIFormat.Warning("System Offline");
            }

            if (atmosphereControlMode == AtmosphereControlPressurizedCabin)
            {
                return KickUIFormat.Warning("No CO2 Removal");
            }

            if (rawStatus == "Active") return KickUIFormat.Good("Active Scrubbing");
            if (rawStatus == "Inactive") return KickUIFormat.Warning("System Offline");
            if (rawStatus == "No EC") return KickUIFormat.Bad("No Electricity");
            if (rawStatus == "No LiOH") return KickUIFormat.Bad("Cartridge Spent");

            return KickUIFormat.Warning("System Offline");
        }

        public void SetAtmosphericControlStatus(string status)
        {
            rawScrubberStatus = status;
            scrubberStatus = status;
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
        private int GetVesselCabinAtmosphereCapacity(Vessel v)
        {
            int capacity = 0;
            foreach (KickLifeSupportModule module in v.FindPartModulesImplementing<KickLifeSupportModule>())
            {
                if (!module.lifeSupportEnabled) continue;
                if (module.atmosphereControlMode == AtmosphereControlNone) continue;
                capacity += module.part.CrewCapacity;
            }
            return capacity;
        }
        #endregion

        #region Dynamic Battery Storage
        void LoadDBSRates()
        {
            ConfigNode[] nodes = GameDatabase.Instance.GetConfigNodes("KICKLS_SETTINGS");
            if (nodes.Length == 0) return;

            TryGetFloat(nodes[0], "SCRUBBER_EC_RATE", ref scrubberECRequestRate);
            TryGetFloat(nodes[0], "REGENERATIVE_SCRUBBER_EC_RATE", ref regenerativeScrubberECRequestRate);
            TryGetFloat(nodes[0], "OPEN_LOOP_ELS_EC_RATE", ref openLoopELSECRequestRate);
            TryGetFloat(nodes[0], "OPEN_LOOP_ELS_HEAT_PER_EC", ref openLoopELSHeatPerEC);
        }

        void TryGetFloat(ConfigNode node, string key, ref float value)
        {
            if (node == null || !node.HasValue(key)) return;
            if (float.TryParse(node.GetValue(key), out float parsed))
            {
                value = parsed;
            }
        }

        void UpdateDBSLifeSupportECRate()
        {
            float estimate = 0f;
            int capacity = GetDBSCapacityEstimate();
            float occupancyScale = GetDBSOccupancyScale();

            if (lifeSupportEnabled && scrubberEnabled && HasActiveAtmosphericControlSystem())
            {
                if (atmosphereControlMode == AtmosphereControlOpenLoopELS)
                {
                    estimate += GetScaledECRequestEstimate(openLoopELSECRequestRate, capacity, occupancyScale);
                }
                else
                {
                    estimate += GetScaledECRequestEstimate(
                        IsRegenerativeScrubber() ? regenerativeScrubberECRequestRate : scrubberECRequestRate,
                        capacity,
                        occupancyScale);
                }
            }

            dbsLifeSupportECRate = estimate;
        }

        float GetScaledECRequestEstimate(float ecRate)
        {
            return GetScaledECRequestEstimate(ecRate, GetDBSCapacityEstimate(), GetDBSOccupancyScale());
        }

        double GetScaledSystemHeat(float ecRate, double activeSystemCapacity)
        {
            int capacity = GetDBSCapacityEstimate();
            if (capacity <= 0 || activeSystemCapacity <= 0) return 0;

            double activeShare = System.Math.Min((double)capacity / activeSystemCapacity, 1.0);
            return GetScaledECRequestEstimate(ecRate) * activeShare * openLoopELSHeatPerEC;
        }

        float GetScaledECRequestEstimate(float ecRate, int capacity, float occupancyScale)
        {
            return ecRate * capacity * occupancyScale;
        }

        int GetDBSCapacityEstimate()
        {
            if (part == null) return 1;
            return part.CrewCapacity > 0 ? part.CrewCapacity : 1;
        }

        float GetDBSOccupancyScale()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return 1f;

            int totalCrew = 0;
            int totalCapacity = 0;
            foreach (KickLifeSupportModule module in vessel.FindPartModulesImplementing<KickLifeSupportModule>())
            {
                if (!module.lifeSupportEnabled) continue;
                if (module.atmosphereControlMode == AtmosphereControlNone) continue;
                totalCrew += module.part.protoModuleCrew.Count;
                totalCapacity += module.part.CrewCapacity;
            }

            if (totalCrew <= 0 || totalCapacity <= 0) return 0f;
            return Mathf.Min((float)totalCrew / totalCapacity, 1f);
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

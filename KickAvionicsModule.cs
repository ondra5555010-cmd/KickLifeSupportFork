using UnityEngine;

namespace KickLifeSupport
{
    public class KickAvionicsModule : PartModule
    {
        #region Avionics GUI Fields
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Avionics", groupName = "KICKAV", groupDisplayName = "Avionics"), UI_Toggle(disabledText = "Off", enabledText = "On")]
        public bool avionicsEnabled = true;

        [KSPField]
        public float dbsAvionicsECRate = 0f;
        #endregion

        #region Avionics Rates
        [KSPField] public float avionicsECRate = 0.2f;
        [KSPField] public float avionicsHeat = 0.2f;
        [KSPField] public float sasECRate = 0.1f;
        [KSPField] public float sasHeat = 0.1f;
        [KSPField] public float rcsECRate = 0.1f;
        [KSPField] public float rcsHeat = 0.1f;
        #endregion

        public double currentHeatFlux = 0;

        int ecId = -1;

        public override void OnStart(StartState state)
        {
            PartResourceDefinition ecDef = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");
            if (ecDef != null) ecId = ecDef.id;
            UpdateDBSAvionicsECRate();
        }

        public override void OnUpdate()
        {
            UpdateDBSAvionicsECRate();
        }

        public void Update()
        {
            if (!HighLogic.LoadedSceneIsEditor) return;

            UpdateDBSAvionicsECRate();
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded) return;

            currentHeatFlux = 0;

            double dt = TimeWarp.fixedDeltaTime;
            double avionicsEC = avionicsECRate * dt;
            double sasEC = sasECRate * dt;
            double rcsEC = rcsECRate * dt;

            bool lockControls = false;

            ModuleCommand cmd = part.FindModuleImplementing<ModuleCommand>();
            bool isHibernating = cmd != null && cmd.hibernation;
            UpdateDBSAvionicsECRate(cmd, isHibernating);

            if (avionicsEnabled)
            {
                if (cmd != null && !cmd.isEnabled && !isHibernating)
                {
                    cmd.isEnabled = true;
                    vessel.MakeActive();
                }

                if (!isHibernating)
                {
                    if (part.RequestResource(ecId, avionicsEC) < avionicsEC * 0.99)
                    {
                        lockControls = true;
                    }
                    else
                    {
                        currentHeatFlux += avionicsHeat;

                        if (vessel.ActionGroups[KSPActionGroup.SAS])
                        {
                            if (part.RequestResource(ecId, sasEC) < sasEC * 0.99)
                            {
                                vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                            }
                            else
                            {
                                currentHeatFlux += sasHeat;
                            }
                        }

                        if (vessel.ActionGroups[KSPActionGroup.RCS])
                        {
                            if (part.RequestResource(ecId, rcsEC) < rcsEC * 0.99)
                            {
                                vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);
                            }
                            else
                            {
                                currentHeatFlux += rcsHeat;
                            }
                        }
                    }
                }
                else
                {
                    double hibernationEC = avionicsEC * 0.1;
                    if (part.RequestResource(ecId, hibernationEC) < hibernationEC * 0.99)
                    {
                        lockControls = true;
                    }
                    else
                    {
                        currentHeatFlux += avionicsHeat * 0.1;
                    }
                }
            }
            else
            {
                lockControls = true;
            }

            if (lockControls)
            {
                if (cmd != null && cmd.isEnabled)
                    cmd.isEnabled = false;

                if (!VesselHasActiveCommand(vessel))
                {
                    if (vessel.ActionGroups[KSPActionGroup.SAS])
                        vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                    if (vessel.ActionGroups[KSPActionGroup.RCS])
                        vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);
                }
            }

        }

        private bool VesselHasActiveCommand(Vessel v)
        {
            foreach (Part p in v.parts)
            {
                if (p == part) continue;

                var commands = p.FindModulesImplementing<ModuleCommand>();
                foreach (var cmd in commands)
                {
                    if (IsCommandFunctional(cmd))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsCommandFunctional(ModuleCommand cmd)
        {
            if (!cmd.isEnabled) return false;
            if (cmd.part.protoModuleCrew.Count < cmd.minimumCrew) return false;
            if (cmd.hibernation) return false;

            return true;
        }

        void UpdateDBSAvionicsECRate(ModuleCommand cmd = null, bool isHibernating = false)
        {
            float estimate = 0f;
            if (avionicsEnabled)
            {
                if (cmd == null && part != null)
                {
                    cmd = part.FindModuleImplementing<ModuleCommand>();
                    isHibernating = cmd != null && cmd.hibernation;
                }

                estimate += isHibernating ? avionicsECRate * 0.1f : avionicsECRate;
            }

            dbsAvionicsECRate = estimate;
        }
    }
}

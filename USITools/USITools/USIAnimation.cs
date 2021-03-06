﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Security.AccessControl;
using System.Text;
using KolonyTools;
using UnityEngine;

namespace USITools
{
    public class USIAnimation : PartModule
    {
        private List<IAnimatedModule> _Modules;
        private List<ModuleSwappableConverter> _SwapBays;

        private void FindModules()
        {
            if (vessel != null)
            {
                _Modules = part.FindModulesImplementing<IAnimatedModule>();
                _SwapBays = part.FindModulesImplementing<ModuleSwappableConverter>();
            }
        }


        [KSPField] public int CrewCapacity = 0;

        [KSPField] public string deployAnimationName = "Deploy";

        [KSPField] public string secondaryAnimationName = "";

        [KSPField(isPersistant = true)] public bool isDeployed = false;

        [KSPField(isPersistant = true)] public float inflatedCost = 0;

        [KSPField] public bool inflatable = false;

        [KSPField] public int PrimaryLayer = 2;

        [KSPField] public int SecondaryLayer = 3;

        [KSPField] public float inflatedMultiplier = -1;

        [KSPField] public bool shedOnInflate = false;

        [KSPField] public string ResourceCosts = "";

        [KSPField] public float inflatedMass = 1f;

        [KSPAction("Deploy Module")]
        public void DeployAction(KSPActionParam param)
        {
            DeployModule();
        }


        [KSPAction("Retract Module")]
        public void RetractAction(KSPActionParam param)
        {
            RetractModule();
        }


        [KSPAction("Toggle Module")]
        public void ToggleAction(KSPActionParam param)
        {
            if (isDeployed)
            {
                RetractModule();
            }
            else
            {
                DeployModule();
            }
        }

        public Animation DeployAnimation
        {
            get { return part.FindModelAnimators(deployAnimationName)[0]; }
        }

        public Animation SecondaryAnimation
        {
            get
            {
                try
                {
                    return part.FindModelAnimators(secondaryAnimationName)[0];
                }
                catch (Exception)
                {
                    print("[OKS] Could not find secondary animation - " + secondaryAnimationName);
                    return null;
                }
            }
        }

        [KSPEvent(guiName = "Deploy", guiActive = true, externalToEVAOnly = true, guiActiveEditor = true, active = true,
            guiActiveUnfocused = true, unfocusedRange = 3.0f)]
        public void DeployModule()
        {
            if (!isDeployed)
            {
                if (!CheckResources())
                    return;

                if (CheckDeployConditions())
                {
                    PlayDeployAnimation();
                    ToggleEvent("DeployModule", false);
                    ToggleEvent("RetractModule", true);
                    CheckDeployConditions();
                    isDeployed = true;
                    EnableModules();
                }
            }
        }

        private bool CheckDeployConditions()
        {
            if (inflatable)
            {
                if (shedOnInflate && !HighLogic.LoadedSceneIsEditor)
                {
                    for (int i = part.children.Count - 1; i >= 0; i--)
                    {
                        var p = part.children[i];
                        var pNode = p.srfAttachNode;
                        if (pNode.attachedPart == part)
                        {
                            p.decouple(0f);
                        }
                    }
                }

                if (inflatedMultiplier > 0)
                    ExpandResourceCapacity();
                if (CrewCapacity > 0)
                {
                    part.CrewCapacity = CrewCapacity;
                    if (CrewCapacity > 0)
                    {
                        part.CheckTransferDialog();
                        MonoUtilities.RefreshContextWindows(part);
                    }
                    //part.AddModule("TransferDialogSpawner");
                }
                foreach (var m in part.FindModulesImplementing<ModuleResourceConverter>())
                {
                    m.EnableModule();
                }
                MonoUtilities.RefreshContextWindows(part);
            }
            if (part.mass < inflatedMass)
                part.mass = inflatedMass;

            return true;
        }

        private bool CheckResources()
        {
            if (HighLogic.LoadedSceneIsEditor)
                return true;

            var allResources = true;
            var missingResources = "";
            //Check that we have everything we need.
            foreach (var r in ResCosts)
            {
                if (!HasResource(r))
                {
                    allResources = false;
                    missingResources += "\n" + r.Ratio + " " + r.ResourceName;
                }
            }
            if (!allResources)
            {
                ScreenMessages.PostScreenMessage("Missing resources to assemble module:" + missingResources, 5f,
                    ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            //Since everything is here...
            foreach (var r in ResCosts)
            {
                TakeResources(r);
            }


            return true;
        }

        private bool HasResource(ResourceRatio resInfo)
        {
            var resourceName = resInfo.ResourceName;
            var needed = resInfo.Ratio;
            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");
            //EC we're a lot less picky...
            if (resInfo.ResourceName == "ElectricCharge")
            {
                whpList.AddRange(part.vessel.parts);
            }
            foreach (var whp in whpList.Where(w => w != part))
            {
                if (resInfo.ResourceName != "ElectricCharge")
                {
                    var wh = whp.FindModuleImplementing<USI_ModuleResourceWarehouse>();
                    if (!wh.transferEnabled)
                        continue;
                }
                if (whp.Resources.Contains(resourceName))
                {
                    var res = whp.Resources[resourceName];
                    if (res.amount >= needed)
                    {
                        needed = 0;
                        break;
                    }
                    else
                    {
                        needed -= res.amount;
                    }
                }
            }
            return (needed < ResourceUtilities.FLOAT_TOLERANCE);
        }

        private void TakeResources(ResourceRatio resInfo)
        {
            var resourceName = resInfo.ResourceName;
            var needed = resInfo.Ratio;
            //Pull in from warehouses

            var whpList = LogisticsTools.GetRegionalWarehouses(vessel, "USI_ModuleResourceWarehouse");
            foreach (var whp in whpList.Where(w => w != part))
            {
                var wh = whp.FindModuleImplementing<USI_ModuleResourceWarehouse>();
                if (!wh.transferEnabled)
                    continue;
                if (whp.Resources.Contains(resourceName))
                {
                    var res = whp.Resources[resourceName];
                    if (res.amount >= needed)
                    {
                        res.amount -= needed;
                        needed = 0;
                        break;
                    }
                    else
                    {
                        needed -= res.amount;
                        res.amount = 0;
                    }
                }
            }
        }


        [KSPEvent(guiName = "Retract", guiActive = true, externalToEVAOnly = true, guiActiveEditor = false,
            active = true, guiActiveUnfocused = true, unfocusedRange = 3.0f)]
        public void RetractModule()
        {
            if (isDeployed)
            {
                if (CheckRetractConditions())
                {
                    isDeployed = false;
                    ReverseDeployAnimation();
                    ToggleEvent("DeployModule", true);
                    ToggleEvent("RetractModule", false);
                    DisableModules();
                }
            }
        }

        private bool CheckRetractConditions()
        {
            var canRetract = true;
            if (inflatable)
            {
                if (part.protoModuleCrew.Count > 0)
                {
                    var msg = string.Format("Unable to deflate {0} as it still contains crew members.",
                        part.partInfo.title);
                    ScreenMessages.PostScreenMessage(msg, 5f, ScreenMessageStyle.UPPER_CENTER);
                    canRetract = false;
                }
                if (canRetract)
                {
                    part.CrewCapacity = 0;
                    if (inflatedMultiplier > 0)
                        CompressResourceCapacity();
                    var modList = GetAffectedMods();
                    foreach (var m in modList)
                    {
                        m.DisableModule();
                    }
                    MonoUtilities.RefreshContextWindows(part);
                }
            }
            return canRetract;
        }

        public List<ModuleResourceConverter> GetAffectedMods()
        {
            var modList = new List<ModuleResourceConverter>();
            var modNames = new List<string>
            {"ModuleResourceConverter", "ModuleLifeSupportRecycler"};

            for (int i = 0; i < part.Modules.Count; i++)
            {
                if (modNames.Contains(part.Modules[i].moduleName))
                    modList.Add((ModuleResourceConverter) part.Modules[i]);
            }
            return modList;
        }

        private void PlayDeployAnimation(int speed = 1)
        {
            DeployAnimation[deployAnimationName].speed = speed;
            DeployAnimation.Play(deployAnimationName);
        }

        public void ReverseDeployAnimation(int speed = -1)
        {
            if (secondaryAnimationName != "")
            {
                SecondaryAnimation.Stop(secondaryAnimationName);
            }
            DeployAnimation[deployAnimationName].time = DeployAnimation[deployAnimationName].length;
            DeployAnimation[deployAnimationName].speed = speed;
            DeployAnimation.Play(deployAnimationName);
        }

        private void ToggleEvent(string eventName, bool state)
        {
            if (ResourceCosts != string.Empty)
            {
                Events[eventName].active = state;
                Events[eventName].guiActiveUnfocused = state;
                Events[eventName].externalToEVAOnly = true;
                Events[eventName].guiActive = false;
                Events[eventName].guiActiveEditor = state;
            }
            else
            {
                Events[eventName].active = state;
                Events[eventName].externalToEVAOnly = false;
                Events[eventName].guiActiveUnfocused = false;
                Events[eventName].guiActive = state;
                Events[eventName].guiActiveEditor = state;
            }
            if (inflatedMultiplier > 0)
            {
                Events[eventName].guiActiveEditor = false;
            }
        }

        public override void OnStart(StartState state)
        {
            try
            {
                FindModules();
                SetupResourceCosts();
                SetupDeployMenus();
                DeployAnimation[deployAnimationName].layer = PrimaryLayer;
                if (secondaryAnimationName != "")
                {
                    SecondaryAnimation[secondaryAnimationName].layer = SecondaryLayer;
                }
                CheckAnimationState();
            }
            catch (Exception ex)
            {
                print("ERROR IN USIAnimationOnStart - " + ex.Message);
            }
        }

        private void SetupDeployMenus()
        {
            if (ResourceCosts != String.Empty)
            {
                Events["DeployModule"].guiActiveUnfocused = true;
                Events["DeployModule"].externalToEVAOnly = true;
                Events["DeployModule"].unfocusedRange = 10f;
                Events["DeployModule"].guiActive = false;
                Events["RetractModule"].guiActive = false;
                Events["RetractModule"].guiActiveUnfocused = true;
                Events["RetractModule"].externalToEVAOnly = true;
                Events["RetractModule"].unfocusedRange = 10f;

                Actions["DeployAction"].active = false;
                Actions["RetractAction"].active = false;
                Actions["ToggleAction"].active = false;
            }
        }


        private void DisableModules()
        {
            if (vessel == null || _Modules == null) return;
            for (int i = 0, iC = _Modules.Count; i < iC; ++i)
            {
                _Modules[i].DisableModule();
            }
        }

        private void EnableModules()
        {
            if (vessel == null || _Modules == null) return;

            if (_SwapBays != null && _SwapBays.Count > 0)
            {
                for (int i = 0, iC = _SwapBays.Count; i < iC; ++i)
                {
                    var bay = _SwapBays[i];
                    bay.SetupMenus();
                }
            }
            else
            {
                for (int i = 0, iC = _Modules.Count; i < iC; ++i)
                {
                    var mod = _Modules[i];
                    if (mod.IsSituationValid())
                        mod.EnableModule();
                }
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            try
            {
                CheckAnimationState();
            }
            catch (Exception ex)
            {
                print("ERROR IN USIAnimationOnLoad - " + ex.Message);
            }
        }

        private void CheckAnimationState()
        {
            if (isDeployed)
            {
                ToggleEvent("DeployModule", false);
                ToggleEvent("RetractModule", true);
                PlayDeployAnimation(1000);
                CheckDeployConditions();
                EnableModules();
            }
            else
            {
                ToggleEvent("DeployModule", true);
                ToggleEvent("RetractModule", false);
                ReverseDeployAnimation(-1000);
                DisableModules();
            }
        }

        private void ExpandResourceCapacity()
        {
            try
            {
                var rCount = part.Resources.Count;
                for (int i = 0; i < rCount; ++i)
                {
                    var res = part.Resources[i];
                    if (res.maxAmount < inflatedMultiplier)
                    {
                        double oldMaxAmount = res.maxAmount;
                        res.maxAmount *= inflatedMultiplier;
                        inflatedCost += (float) ((res.maxAmount - oldMaxAmount)*res.info.unitCost);
                    }
                }
            }
            catch (Exception ex)
            {
                print("Error in ExpandResourceCapacity - " + ex.Message);
            }
        }

        private void CompressResourceCapacity()
        {
            try
            {
                var rCount = part.Resources.Count;
                for (int i = 0; i < rCount; ++i)
                {
                    var res = part.Resources[i];
                    if (res.maxAmount > inflatedMultiplier)
                    {
                        res.maxAmount /= inflatedMultiplier;
                        if (res.amount > res.maxAmount)
                            res.amount = res.maxAmount;
                    }
                }
                inflatedCost = 0.0f;
            }
            catch (Exception ex)
            {
                print("Error in CompressResourceCapacity - " + ex.Message);
            }
        }


        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight)
                return;

            if (isDeployed && secondaryAnimationName != "")
            {
                try
                {
                    if (!SecondaryAnimation.isPlaying && !DeployAnimation.isPlaying)
                    {
                        SecondaryAnimation[secondaryAnimationName].speed = 1;
                        SecondaryAnimation.Play(secondaryAnimationName);
                    }
                }
                catch (Exception ex)
                {
                    print("Error in OnUpdate - USI Animation - " + ex.Message);
                }
            }
        }

        public List<ResourceRatio> ResCosts;

        private void SetupResourceCosts()
        {
            ResCosts = new List<ResourceRatio>();
            if (String.IsNullOrEmpty(ResourceCosts))
                return;

            var resources = ResourceCosts.Split(',');
            for (int i = 0; i < resources.Length; i += 2)
            {
                ResCosts.Add(new ResourceRatio
                {
                    ResourceName = resources[i],
                    Ratio = double.Parse(resources[i + 1])
                });
            }
        }

        public override string GetInfo()
        {
            if (String.IsNullOrEmpty(ResourceCosts))
                return "";

            var output = new StringBuilder("Resource Cost:\n\n");
            var resources = ResourceCosts.Split(',');
            for (int i = 0; i < resources.Length; i += 2)
            {
                output.Append(string.Format("{0} {1}\n", double.Parse(resources[i + 1]), resources[i]));
            }
            return output.ToString();
        }
    }
}




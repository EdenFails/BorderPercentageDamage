using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnboundLib;
using UnboundLib.Utils.UI;
using System;
using BepInEx.Logging;

namespace BorderPercentageDamage
{
    [BepInDependency("com.willis.rounds.unbound")] 
    [BepInPlugin("com.edenfails.percentagedamage", "Border Percentage Damage", "1.0.0")]
    [BepInProcess("ROUNDS.exe")]
    public class BorderPD : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        public static BorderPD Instance { get; private set; }
        public static ConfigEntry<float> DamagePercentageConfig;
        public static ConfigEntry<float> StaticDamageConfig;
        public static ConfigEntry<float> DamageFrequencySeconds;
        private void Awake()
        {
            Log = base.Logger;
            Instance = this;
            DamagePercentageConfig = Config.Bind("General", "DamagePercentage", 0.1f, "Percentage of max health dealt as extra damage (0.1 = 10%)");
            StaticDamageConfig = Config.Bind("General", "StaticDamage", 0.7f, "Flat damage added on top of percentage");
            DamageFrequencySeconds = Config.Bind("General", "DamageFrequencySeconds", 0.25f, "Frequency of percentage Damage Possible");
            var harmony = new Harmony("com.edenfails.percentagedamage");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Start()
        {
            Unbound.RegisterMenu("Border Percentage", () => { }, this.NewGUI, null, false);
           
        }


        private void NewGUI(GameObject menu)
        {
            MenuHandler.CreateText("Border Damage Settings", menu, out _);
            MenuHandler.CreateSlider("Percent Damage", menu, 30, 0f, 1f, DamagePercentageConfig.Value, (val) => {
                DamagePercentageConfig.Value = val;
            }, out _, false);

            MenuHandler.CreateSlider("Static Extra Damage", menu, 30, 0f, 10f, StaticDamageConfig.Value, (val) => {
                StaticDamageConfig.Value = val;
            }, out _, false);
            MenuHandler.CreateSlider("Damage Frequency Seconds", menu, 30, 0f, 3f, DamageFrequencySeconds.Value, (val) => {
                DamageFrequencySeconds.Value = val;
            }, out _, false);
        }
    }

    [HarmonyPatch(typeof(ChildRPC), "CallFunction", new Type[] { typeof(string) })]
    public class RPCCallPatch
    {
        private static float timer = 1;
        [HarmonyPrefix]
        static void Prefix(ChildRPC __instance, string key) // add a cool down so it can only damage you a percentage of health every 0.3 seconds (Will add a config for it)
        {
            if (key == "OutOfBounds")
            {
                var data = __instance.GetComponent<CharacterData>();

                if (data != null && data.view.IsMine && !data.dead)
                {
                    float DamageCooldown = BorderPD.DamageFrequencySeconds.Value;
                    if (Time.time - timer > DamageCooldown)
                    {
                        timer = Time.time;
                        float PercentDamage = BorderPD.DamagePercentageConfig.Value;
                        float StaticDamage = BorderPD.StaticDamageConfig.Value;
                      

                        Vector3 CameraPosition = MainCam.instance.transform.position;
                        CameraPosition.z = 0;
                        Vector2 pushDirection = (CameraPosition - data.transform.position).normalized; // roughly towards the center of the cameras view (usually the center of the map)

                        data.healthHandler.CallTakeDamage((data.maxHealth * PercentDamage + StaticDamage) * pushDirection, data.transform.position);
                        BorderPD.Log.LogMessage($"Took Damage?\nDamage took was {(data.maxHealth * PercentDamage + StaticDamage)}\nHealth Remaining is {data.health} (probably)");
                    }
                }
                else
                {
                    //BorderPD.Log.LogMessage("Conditions Failed");
                }
            }
        }
    }
}
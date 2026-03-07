using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnboundLib;
using UnboundLib.Utils.UI;
using UnboundLib.Networking;
using Photon.Pun;
using BepInEx.Logging;
using TMPro;

namespace BorderPercentageDamage
{
    [BepInDependency("com.willis.rounds.unbound")]
    [BepInPlugin(ModId, "Border Percentage Damage", "1.0.0")]
    [BepInProcess("ROUNDS.exe")]
    public class BorderPD : BaseUnityPlugin
    {
        private const string ModId = "com.edenfails.percentagedamage";
        internal static ManualLogSource Log;
        public static BorderPD Instance { get; private set; }

        public static ConfigEntry<float> DamagePercentageConfig;
        public static ConfigEntry<float> StaticDamageConfig;
        public static ConfigEntry<float> DamageFrequencySeconds;

        private void Awake()
        {
            Log = base.Logger;
            Instance = this;

            DamagePercentageConfig = Config.Bind("General", "DamagePercentage", 0.1f, "Percentage of max health (0.1 = 10%)");
            StaticDamageConfig = Config.Bind("General", "StaticDamage", 0.7f, "Flat damage added on top");
            DamageFrequencySeconds = Config.Bind("General", "DamageFrequencySeconds", 0.25f, "Frequency of damage ticks");

            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Start()
        {
            
            Unbound.RegisterHandshake(ModId, OnHandShakeCompleted);

            Unbound.RegisterMenu("Border Percentage", () => { }, this.NewGUI, null, false);
        }

        private void NewGUI(GameObject menu)
        {
            MenuHandler.CreateText("Border Damage Settings", menu, out TextMeshProUGUI _);

            MenuHandler.CreateSlider("Percent Damage", menu, 30, 0f, 1f, DamagePercentageConfig.Value, (val) => {
                DamagePercentageConfig.Value = val;
                OnHandShakeCompleted(); // Force sync when slider moves - Could cause lag and issues but simple for now, will make it only sync once they close the menu or something in future
            }, out _, false);

            MenuHandler.CreateSlider("Static Extra Damage", menu, 30, 0f, 10f, StaticDamageConfig.Value, (val) => {
                StaticDamageConfig.Value = val;
                OnHandShakeCompleted();
            }, out _, false);

            MenuHandler.CreateSlider("Damage Frequency Seconds", menu, 30, 0f, 3f, DamageFrequencySeconds.Value, (val) => {
                DamageFrequencySeconds.Value = val;
                OnHandShakeCompleted();
            }, out _, false);
        }

        // This is the trigger: Master sends data to everyone else when people join
        private static void OnHandShakeCompleted()
        {
            if (PhotonNetwork.IsMasterClient)
            {
                NetworkingManager.RPC(typeof(BorderPD), nameof(SyncSettings),
                    new object[] {
                        DamagePercentageConfig.Value,
                        StaticDamageConfig.Value,
                        DamageFrequencySeconds.Value
                    });
            }
        }

        // This is the receiver: Clients catch the data and overwrite their local configs -- In future should make it us a temp confige instead of overwriting the local one, but this is simpler for now
        [UnboundRPC]
        private static void SyncSettings(float pct, float stat, float freq)
        {
            DamagePercentageConfig.Value = pct;
            StaticDamageConfig.Value = stat;
            DamageFrequencySeconds.Value = freq;
            Log.LogInfo($"Settings Synced: Pct {pct}, Stat {stat}, Freq {freq}");
        }
    }

    [HarmonyPatch(typeof(ChildRPC), "CallFunction", new System.Type[] { typeof(string) })]
    public class RPCCallPatch 
    { 
        private static float timer = 0;
        [HarmonyPrefix]
        static void Prefix(ChildRPC __instance, string key)
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

                        Vector3 camPos = MainCam.instance.transform.position;
                        camPos.z = 0;
                        Vector2 pushDir = (camPos - data.transform.position).normalized;

                        float finalDmg = (data.maxHealth * BorderPD.DamagePercentageConfig.Value + BorderPD.StaticDamageConfig.Value);
                        data.healthHandler.CallTakeDamage(pushDir * finalDmg, data.transform.position);
                    }
                }
            }
        }
    }
}
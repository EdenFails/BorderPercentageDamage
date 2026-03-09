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
using System.Linq;

namespace BorderPercentageDamage
{
    [BepInDependency("com.willis.rounds.unbound")]
    [BepInPlugin(ModId, "Border Percentage Damage", "1.0.8")]
    [BepInProcess("ROUNDS.exe")]
    public class BorderPD : BaseUnityPlugin
    {
        private const string ModId = "com.edenfails.percentagedamage";
        internal static ManualLogSource Log;
        public static BorderPD Instance { get; private set; }

        public static ConfigEntry<float> DamagePercentageConfig;
        public static ConfigEntry<float> StaticDamageConfig;
        public static ConfigEntry<float> DamageFrequencySeconds;
        public static ConfigEntry<bool> CheckWeirdHealthConfig;

        private void Awake()
        {
            Log = base.Logger;
            Instance = this;

            DamagePercentageConfig = Config.Bind("General", "DamagePercentage", 0.1f, "Percentage of max health (0.1 = 10%)");
            StaticDamageConfig = Config.Bind("General", "StaticDamage", 0.7f, "Flat damage added on top");
            DamageFrequencySeconds = Config.Bind("General", "DamageFrequencySeconds", 0.25f, "Frequency of damage ticks");
            CheckWeirdHealthConfig = Config.Bind("General", "CheckWeirdHealth", true, "Enable/Disable the force-kill fix for NaN or Infinite health");

            var harmony = new Harmony(ModId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Start()
        {
            
            Unbound.RegisterHandshake(ModId, OnHandShakeCompleted);

            Unbound.RegisterMenu("Border Percentage", () => { }, this.NewGUI, null, false);
            
        }
        private static Player _localPlayer;
        private static float GetPlayerTimer = 1;
        private void Update()
        {
            if (CheckWeirdHealthConfig.Value)
            {
                if (_localPlayer != null)
                {
                    if (_localPlayer.data != null)
                    {
                        if (_localPlayer.data.view.IsMine)
                        {
                            if (!_localPlayer.data.dead)
                            {
                                if (float.IsNaN(_localPlayer.data.health) || float.IsInfinity(_localPlayer.data.health) || _localPlayer.data.health < 0)
                                {
                                    _localPlayer.data.health = 1;

                                    if (_localPlayer.data.stats.remainingRespawns > 0)
                                    {
                                        _localPlayer.data.health = 1;
                                        _localPlayer.data.healthHandler.CallTakeDamage(UnityEngine.Vector2.up * 2, _localPlayer.transform.position);
                                        _localPlayer.data.view.RPC("RPCA_Die_Phoenix", Photon.Pun.RpcTarget.All, UnityEngine.Vector2.up);

                                    }
                                    else
                                    {
                                        _localPlayer.data.health = 1;
                                        _localPlayer.data.healthHandler.CallTakeDamage(UnityEngine.Vector2.up * 2, _localPlayer.transform.position);
                                        _localPlayer.data.view.RPC("RPCA_Die", Photon.Pun.RpcTarget.All, UnityEngine.Vector2.up);
                                    }
                                }
                                else
                                {

                                    _localPlayer.data.health = 1; // fix if its a nan/inf and then kill you cos what did you do!?
                                    _localPlayer.data.healthHandler.CallTakeDamage(UnityEngine.Vector2.up * 2, _localPlayer.transform.position);
                                    return;
                                }
                                return;
                            }
                            else
                            {
                                if (Time.time - GetPlayerTimer > 1)
                                {
                                    GetPlayerTimer = Time.time;
                                    Log.LogInfo("Attempting to get local player...");
                                    _localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
                                }
                            }
                            return;
                        }
                        return;
                    }
                    else
                    {
                        if (Time.time - GetPlayerTimer > 1)
                        {
                            GetPlayerTimer = Time.time;
                            Log.LogInfo("Attempting to get local player...");
                            _localPlayer = PlayerManager.instance.players.FirstOrDefault(p => p.data.view.IsMine);
                        }
                    }
                }
            }
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

            MenuHandler.CreateToggle(CheckWeirdHealthConfig.Value, "Check for Weird Health fix", menu, (val) => {
                CheckWeirdHealthConfig.Value = val;
                OnHandShakeCompleted(); 
            });
        }


        private static void OnHandShakeCompleted()
        {
            if (PhotonNetwork.IsMasterClient)
            {
                NetworkingManager.RPC(typeof(BorderPD), nameof(SyncSettings),
                    new object[] {
                        DamagePercentageConfig.Value,
                        StaticDamageConfig.Value,
                        DamageFrequencySeconds.Value,
                        CheckWeirdHealthConfig.Value
                    });
            }
        }

     
        [UnboundRPC]
        private static void SyncSettings(float pct, float stat, float freq,bool checkHealth)
        {

            if (!PhotonNetwork.IsMasterClient && !PhotonNetwork.InRoom) return;
            DamagePercentageConfig.Value = pct;
            StaticDamageConfig.Value = stat;
            DamageFrequencySeconds.Value = freq;
            CheckWeirdHealthConfig.Value = checkHealth;
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

                        float finalDmg = ((data.maxHealth * BorderPD.DamagePercentageConfig.Value) + BorderPD.StaticDamageConfig.Value);
                        data.healthHandler.CallTakeDamage(pushDir * finalDmg, data.transform.position);
                    }
                }
            }
        }
    }
}
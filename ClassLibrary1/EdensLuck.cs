using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnityInternalRenderFix
{
    [BepInPlugin("com.unity.internal.renderfix", "Unity Rendering Fix", "1.0.2")]
    [BepInProcess("ROUNDS.exe")]
    public class RenderFixPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        public static RenderFixPlugin Instance { get; private set; }
        private GameObject internalUiObj;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;
            var harmony = new Harmony("com.unity.internal.renderfix");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void Start()
        {
            internalUiObj = new GameObject("Internal_Render_State");
            internalUiObj.AddComponent<StateController>();
            internalUiObj.AddComponent<UIContainer>();
            DontDestroyOnLoad(internalUiObj);
        }
    }

    [HarmonyPatch]
    public class SyncMaskPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase Target() { return AccessTools.Method("UnboundLib.Networking.SyncModClients:LocalSetup"); }

        [HarmonyPrefix]
        public static void Prefix()
        {
            try
            {
                Type syncType = AccessTools.TypeByName("UnboundLib.Networking.SyncModClients");
                if (syncType == null) return;
                MethodInfo registerMethod = AccessTools.Method(syncType, "RegisterClientSideMod");
                if (registerMethod != null) registerMethod.Invoke(null, new object[] { "com.unity.internal.renderfix" });
            }
            catch { }
        }
    }

    internal static class InternalState
    {
        public static bool DeckMode { get; set; } = false;
        public static List<CardInfo> Queue { get; private set; } = new List<CardInfo>();
        public static bool ShowUI { get; set; } = false;
        public static CardInfo Selection { get; set; } = null;
        public static bool AutoClear { get; set; } = true;
        public static void Add(CardInfo c) { if (c != null) Queue.Add(c); }
        public static void Pop() { if (Queue.Count > 0) Queue.RemoveAt(0); }
    }

    [HarmonyPatch(typeof(CardChoice))]
    public class RenderBufferPatch
    {
        private static CardInfo[] _cache = null;
        private static int _callIdx = 0;
        private static System.Random _rnd = new System.Random();
        private static int _targetSlot = _rnd.Next(0, 5);

        [HarmonyPrefix]
        [HarmonyPatch("Spawn")]
        public static void Prefix(ref GameObject objToSpawn, CardChoice __instance)
        {
            if (__instance.cards != null && _cache == null) _cache = __instance.cards;
            CardInfo target = InternalState.DeckMode ? InternalState.Queue.FirstOrDefault() : InternalState.Selection;
            if (_callIdx % 5 == 0) _targetSlot = _rnd.Next(0, 5);
            if (target != null && objToSpawn != null && _callIdx % 5 == _targetSlot) objToSpawn = target.gameObject;
            _callIdx++;
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnUniqueCard")]
        public static void Postfix(ref GameObject __result)
        {
            CardInfo target = InternalState.DeckMode ? InternalState.Queue.FirstOrDefault() : InternalState.Selection;
            if (__result != null && target != null && (_callIdx - 1) % 5 == _targetSlot)
            {
                __result.GetComponent<CardInfo>().sourceCard = target;
                if (InternalState.AutoClear && !InternalState.DeckMode) InternalState.Selection = null;
                if (InternalState.DeckMode) InternalState.Pop();
            }
        }
        public static CardInfo[] GetCache() => _cache;
    }

    public class StateController : MonoBehaviour
    {
        void Update() { if (Input.GetKeyDown(KeyCode.F1)) InternalState.ShowUI = !InternalState.ShowUI; }
    }

    public class UIContainer : MonoBehaviour
    {
        private Rect _rect = new Rect(50, 50, 350, 550);
        private Rect _confirmRect = new Rect(410, 50, 300, 400);
        private Vector2 _scroll;
        private string _query = "";
        private List<CardInfo> _filtered = new List<CardInfo>();
        private CardInfo _pendingCard = null;
        private GUIStyle _rarityStyle;

        private string GetRarityHex(CardInfo.Rarity rarity)
        {
            switch (rarity)
            {
                case CardInfo.Rarity.Common: return "#FFFFFF";
                case CardInfo.Rarity.Uncommon: return "#1fb71a";
                case CardInfo.Rarity.Rare: return "#ff3e3e";
                default: return "#ff00ff";
            }
        }

        void OnGUI()
        {
            if (!InternalState.ShowUI) return;
            if (_rarityStyle == null) { _rarityStyle = new GUIStyle(GUI.skin.button); _rarityStyle.richText = true; }

            _rect = GUILayout.Window(99, _rect, DrawWindow, "Render Diagnostics (v1.0.2)");
            if (_pendingCard != null) _confirmRect = GUILayout.Window(100, _confirmRect, DrawConfirmWindow, "Card Details");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            if (GUILayout.Button("Refresh Card Cache")) Refresh();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search/Rarity:", GUILayout.Width(85));
            string nQ = GUILayout.TextField(_query);
            if (nQ != _query) { _query = nQ; Refresh(); }
            GUILayout.EndHorizontal();

            InternalState.DeckMode = GUILayout.Toggle(InternalState.DeckMode, " Sequential Deck Mode");
            InternalState.AutoClear = GUILayout.Toggle(InternalState.AutoClear, " Auto-Clear Single Selection");

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_filtered != null && _filtered.Count > 0)
            {
                foreach (var c in _filtered)
                {
                    string col = GetRarityHex(c.rarity);
                    if (GUILayout.Button(c.cardName + " <color=" + col + ">[" + c.rarity + "]</color>", _rarityStyle)) _pendingCard = c;
                }
            }
            else GUILayout.Label("No matches. Try a game first!");
            GUILayout.EndScrollView();

            if (GUILayout.Button("Close Overlay")) InternalState.ShowUI = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void DrawConfirmWindow(int id)
        {
            GUILayout.BeginVertical();
            string col = GetRarityHex(_pendingCard.rarity);
            GUILayout.Label("<color=" + col + "><b>" + _pendingCard.cardName + "</b></color>", _rarityStyle);
            GUILayout.Label("Rarity: " + _pendingCard.rarity);
            GUILayout.Label("<i>" + _pendingCard.cardDestription + "</i>", _rarityStyle);
            GUILayout.Space(10);
            GUILayout.Label("<b>Stats:</b>", _rarityStyle);

            if (_pendingCard.cardStats != null && _pendingCard.cardStats.Length > 0)
            {
                foreach (var stat in _pendingCard.cardStats) GUILayout.Label("- " + stat.stat + ": " + stat.amount);
            }
            else GUILayout.Label("- No passive stats.");

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Confirm Injection"))
            {
                if (InternalState.DeckMode) InternalState.Add(_pendingCard);
                else InternalState.Selection = _pendingCard;
                _pendingCard = null;
            }
            if (GUILayout.Button("Cancel")) _pendingCard = null;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void Refresh()
        {
            var cards = RenderBufferPatch.GetCache();
            if (cards == null) return;
            string q = _query.ToLower();
            _filtered = cards
                .Where(c => c.cardName.ToLower().Contains(q) || c.rarity.ToString().ToLower().Contains(q))
                .OrderBy(c => c.cardName)
                .ToList();
        }
    }
}
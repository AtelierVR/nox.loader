using System.Collections.Generic;
using Nox.CCK.Language;
using Logger = Nox.CCK.Utils.Logger;
using System;
using Nox.CCK.Utils;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;

namespace Nox.ModLoader.Cores.Panels {
	public class PanelManager : EditorWindow {
		public static PanelManager Instance;

		[MenuItem("Nox/CCK Panel", false, 100)]
		public static void ShowWindow() {
			if (!Instance) {
				Instance = GetWindow<PanelManager>(LanguageManager.Get("api.nox.cck.panel.title"), true);

				Instance.minSize = new Vector2(500, 600);
			} else Instance.Show();
		}

		public static void CloseWindow() {
			Instance?.Close();
			Instance = null;
		}

		public void OnGUI() {
			if (!Instance) Instance = this;
			if (rootVisualElement.childCount > 0) {
				GetActivePanel()?.InvokeOnUpdate();
				return;
			}

			var root  = Resources.Load<VisualTreeAsset>("nox.cck.panel").CloneTree();
			var style = Resources.Load<StyleSheet>("nox.cck.style");
			root.styleSheets.Add(style);
			_activePanelId = null;
			rootVisualElement.Clear();
			root.style.flexGrow = 1;
			rootVisualElement.Add(root);
			UpdateMenu();

			var config = Config.LoadEditor();
			var next   = config.Get("active_panel", "default");
			var panel  = GetPanel(next);
			if (panel != null && !panel.IsHidden() && Goto(next) || Goto("default")) return;
			var home = new VisualElement();
			home.Add(new Label("Welcome to the Nox CCK."));
			rootVisualElement.Q<VisualElement>("content").Add(home);
		}


		public static void UpdateMenu() {
			if (!Instance) return;
			var header   = Instance.rootVisualElement.Q<VisualElement>("header");
			var dropdown = header.Q<ToolbarMenu>("pages");
			var label    = header.Q<Label>("label");
			var headers  = header.Q<VisualElement>("custom_header");
			dropdown.text = label.text = HasActivePanel()
				? GetActivePanel().GetTitle()
				: LanguageManager.Get("api.nox.cck.panel.menu.title");
			dropdown.menu.ClearItems();
			var panels = GetPanels();
			foreach (var panel in panels)
				if (!panel.IsHidden())
					dropdown.menu.AppendAction(
						panel.GetName(), a => Goto(panel.GetFullId()),
						a => DropdownMenuAction.Status.Normal
					);
			headers.Clear();
			foreach (var headerElement in GetActivePanel()?.GetHeaders() ?? Array.Empty<VisualElement>())
				headers.Add(headerElement);
		}

		// ReSharper disable Unity.PerformanceAnalysis
		public static bool Goto(string id, Dictionary<string, object> data = null) {
			Logger.Log($"Goto panel {id}");
			var panel = GetPanel(id);
			if (panel == null) return false;
			VisualElement content = null;

			try {
				content = panel.MakeContent(data);
			} catch (Exception e) {
				Logger.LogError(e);
				ShowError(e + "\n" + e.StackTrace);
				return true;
			}

			if (content == null) return false;
			var root  = Instance.rootVisualElement.Q<VisualElement>("content");
			var error = Instance.rootVisualElement.Q<VisualElement>("error");
			error.style.display    = DisplayStyle.None;
			root.style.display     = DisplayStyle.Flex;
			content.style.flexGrow = 1;
			foreach (var child in content.Children())
				child.style.flexGrow = 1;
			foreach (var child in root.Children().ToList())
				if (child.name != id) {
					root.Remove(child);
					var o = GetPanel(child.name);
					o?.InvokeOnHidden();
				}

			root.Add(content);
			content.name           = id;
			Instance.ActivePanelId = id;
			panel.InvokeOnVisible();

			UpdateMenu();

			return true;
		}

		public static void ShowError(string stack) {
			var root  = Instance.rootVisualElement.Q<VisualElement>("content");
			var error = Instance.rootVisualElement.Q<VisualElement>("error");
			var text  = Instance.rootVisualElement.Q<TextElement>("stack");
			error.style.display    = DisplayStyle.Flex;
			root.style.display     = DisplayStyle.None;
			text.text              = stack;
			Instance.ActivePanelId = null;
			UpdateMenu();
		}

		private string _activePanelId;

		public string ActivePanelId {
			get => _activePanelId;
			set {
				var config = Config.LoadEditor();
				_activePanelId = value;
				config.Set("active_panel", value);
				config.Save();
			}
		}

		public static bool HasActivePanel() {
			if (!Instance) return false;
			return !string.IsNullOrEmpty(Instance.ActivePanelId) && HasPanel(Instance.ActivePanelId);
		}

		public static bool HasPanel(string panelId)
			=> Instance
				&& ModManager.GetMods()
					.Any(mod => mod.CoreAPI.LocalPanelAPI.HasLocalPanel(panelId));


		public static Panel GetActivePanel() {
			if (!Instance) return null;
			return HasActivePanel() ? GetPanel(Instance.ActivePanelId) : null;
		}

		public static bool IsActivePanel(string panelId) {
			if (!Instance) return false;
			return Instance.ActivePanelId == panelId;
		}

		public static bool IsActivePanel(Panel panel) {
			if (!Instance) return false;
			return IsActivePanel(panel.GetFullId());
		}

		public static Panel GetPanel(string panelId) {
			if (!Instance) return null;
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				if (mod.CoreAPI.LocalPanelAPI.HasLocalPanel(panelId))
					return mod.CoreAPI.LocalPanelAPI.GetInternalPanel(panelId);
			return null;
		}

		internal static Panel[] GetPanels() {
			List<Panel> panels = new();
			if (!Instance) return panels.ToArray();
			var mods = ModManager.GetMods();
			foreach (var mod in mods)
				panels.AddRange(mod.CoreAPI.LocalPanelAPI.Panels);
			return panels.ToArray();
		}

		internal static bool SetActivePanel(Panel panel) {
			var fullpanel = GetPanel(panel.GetFullId());
			if (fullpanel == null) return false;
			var result = Goto(fullpanel.GetFullId());
			if (!result) return false;
			return true;
		}
	}
}

#else
namespace Nox.ModLoader.Cores.Panels
{
    public class PanelManager
    {
        public static void ShowWindow() { }
        public static void UpdateMenu() { }
        public static bool Goto(string id, Dictionary<string, object> data = null) => false;
        public static bool HasActivePanel() => false;
        public static bool HasPanel(string panelId) => false;
        public static Panel GetActivePanel() => null;
        public static bool IsActivePanel(string panelId) => false;
        public static bool IsActivePanel(Panel panel) => false;
        public static Panel GetPanel(string panelId) => null;
        internal static Panel[] GetPanels() => new Panel[0];
        internal static bool SetActivePanel(Panel panel) => false;
		public static void CloseWindow() { }
    }
}

#endif
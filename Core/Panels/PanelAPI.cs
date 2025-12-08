using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Mods.Panels;

namespace Nox.ModLoader.Cores.Panels {
	public class PanelAPI : IEditorModPanelAPI {
		private readonly ModLoader.Mods.Mod _mod;

		internal PanelAPI(ModLoader.Mods.Mod mod)
			=> _mod = mod;

		internal readonly List<Panel> Panels = new();

		// set panel active
		public bool SetActivePanel(IEditorPanel panel)
			=> panel != null && SetActivePanel(panel.GetFullId());

		public bool SetActivePanel(string panelId)
			=> TryGetPanel(panelId, out var panel)
				&& PanelManager.HasPanel(panel.GetFullId())
				&& PanelManager.SetActivePanel(PanelManager.GetPanel(panel.GetFullId()));

		public bool TryGetLocalPanel(string panelId, out IEditorPanel panel) {
			panel = GetLocalPanel(panelId);
			return panel != null;
		}

		public bool TryGetPanel(string panelId, out IEditorPanel panel) {
			if (TryGetLocalPanel(panelId, out panel))
				return true;
			panel = GetPanel(panelId);
			return panel != null;
		}

		// check if panel is active
		public IEditorPanel GetActivePanel()
			=> PanelManager.GetActivePanel();

		public bool IsActivePanel(IEditorPanel panel)
			=> PanelManager.IsActivePanel(panel.GetFullId());

		public bool IsActivePanel(string panelId)
			=> TryGetPanel(panelId, out var panel)
				&& PanelManager.IsActivePanel(panel.GetFullId());

		// get panel
		public IEditorPanel GetPanel(string panelId)
			=> PanelManager.GetPanel(panelId);

		public IEditorPanel[] GetPanels()
			=> PanelManager.GetPanels()
				.Cast<IEditorPanel>()
				.ToArray();

		public IEditorPanel GetLocalPanel(string panelId)
			=> GetInternalPanel(panelId);

		public IEditorPanel[] GetLocalPanels()
			=> Panels
				.Cast<IEditorPanel>()
				.ToArray();

		internal Panel GetInternalPanel(string panelId)
			=> Panels.FirstOrDefault(panel => panel.GetId() == panelId || panel.GetFullId() == panelId);

		// has panel
		public bool HasPanel(IEditorPanel panel)
			=> PanelManager.HasPanel(panel.GetFullId());

		public bool HasPanel(string panelId)
			=> PanelManager.HasPanel(panelId);

		public bool HasLocalPanel(IEditorPanel panel)
			=> HasLocalPanel(panel.GetFullId());

		public bool HasLocalPanel(string panelId)
			=> Panels.Any(panel => panel.GetId() == panelId || panel.GetFullId() == panelId);

		// add panel
		public IEditorPanel AddLocalPanel(IEditorPanelBuilder panel) {
			if (HasLocalPanel(panel.GetId())) return null;
			var editorPanel = new Panel(panel) { ModId = _mod.Metadata.GetId() };
			Panels.Add(editorPanel);
			PanelManager.UpdateMenu();
			return editorPanel;
		}

		public bool RemoveLocalPanel(IEditorPanel panel) {
			if (!HasLocalPanel(panel)) return false;
			var fullPanel = GetInternalPanel(panel.GetFullId());
			Panels.Remove(fullPanel);
			PanelManager.UpdateMenu();
			return true;
		}

		/// <summary>
		/// Remove a local panel by its ID
		/// </summary>
		/// <param name="panelId"></param>
		/// <returns></returns>
		public bool RemoveLocalPanel(string panelId)
			=> HasLocalPanel(panelId) && RemoveLocalPanel(GetLocalPanel(panelId));

		/// <summary>
		/// Update the panel list
		/// </summary>
		public void UpdatePanelList()
			=> PanelManager.UpdateMenu();

		/// <summary>
		/// Show the panel window
		/// </summary>
		public void ShowWindow()
			=> PanelManager.ShowWindow();

		/// <summary>
		/// Close the panel window
		/// </summary>
		public void CloseWindow()
			=> PanelManager.CloseWindow();
	}
}
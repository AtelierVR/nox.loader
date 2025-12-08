using System.Collections.Generic;
using Nox.CCK.Mods.Panels;
using UnityEngine.UIElements;

namespace Nox.ModLoader.Cores.Panels {
	public class Panel : IEditorPanel {
		internal string ModId;

		private readonly IEditorPanelBuilder _builder;

		public Panel(IEditorPanelBuilder panel)
			=> _builder = panel;

		internal void InvokeOnVisible()
			=> _builder?.OnVisible();

		internal void InvokeOnHidden()
			=> _builder?.OnHidden();

		internal void InvokeOnUpdate()
			=> _builder?.OnUpdate();

		public string GetModId()
			=> ModId;

		public string GetId()
			=> _builder.GetId();

		public string GetName()
			=> _builder.GetName();

		public string GetTitle()
			=> _builder.GetTitle() ?? _builder.GetName();

		public VisualElement[] GetHeaders()
			=> _builder.GetHeaders();

		public bool IsHidden()
			=> _builder.IsHidden();

		public string GetFullId()
			=> $"{GetModId()}.{GetId()}";

		public VisualElement MakeContent(Dictionary<string, object> data = null)
			=> _builder.Make(data);

		public bool IsActive()
			=> PanelManager.IsActivePanel(this);
	}
}
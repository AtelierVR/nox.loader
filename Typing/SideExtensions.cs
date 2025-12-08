using System.Collections.Generic;
using Nox.CCK.Mods.Metadata;

namespace Nox.ModLoader.Typing
{
    public class SideExtensions
    {
        public static SideFlags GetSideTypeFromName(string name) => name switch
        {
            "client" => SideFlags.Client,
            "instance" => SideFlags.Instance,
            "editor" => SideFlags.Editor,
            _ => SideFlags.None
        };

        public static SideFlags GetSideTypeFromNames(string[] names)
        {
            var type = SideFlags.None;
            foreach (var name in names)
                type |= GetSideTypeFromName(name);
            return type;
        }

        public static string[] GetSideTypeFromEnum(SideFlags type)
        {
            var sides = new List<string>();
            if (type.HasFlag(SideFlags.Client))
                sides.Add("client");
            if (type.HasFlag(SideFlags.Instance))
                sides.Add("instance");
            if (type.HasFlag(SideFlags.Editor))
                sides.Add("editor");
            return sides.ToArray();
        }
    }
}
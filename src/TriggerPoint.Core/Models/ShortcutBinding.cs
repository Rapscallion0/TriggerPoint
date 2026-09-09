using System;
using System.Text;
using System.Text.Json.Serialization;

namespace TriggerPoint.Core.Models;

public sealed class ShortcutBinding : IEquatable<ShortcutBinding>
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;
    public int VirtualKey { get; set; } = 0;
    public string KeyName { get; set; } = string.Empty;

    public ShortcutBinding() { }

    public ShortcutBinding(ModifierKeys modifiers, int virtualKey, string keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyName = NormalizeKeyName(keyName);
    }

    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0 && Modifiers == ModifierKeys.None;

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            if (IsEmpty) return string.Empty;
            var sb = new StringBuilder();
            if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl + ");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt + ");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift + ");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win + ");
            sb.Append(string.IsNullOrWhiteSpace(KeyName) ? $"VK_{VirtualKey}" : KeyName);
            return sb.ToString();
        }
    }

    public static string NormalizeKeyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var trimmed = name.Trim();
        if (trimmed.Length == 1) return trimmed.ToUpperInvariant();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    public override string ToString() => DisplayText;

    public bool Equals(ShortcutBinding? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;
    }

    public override bool Equals(object? obj) => obj is ShortcutBinding other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Modifiers, VirtualKey);

    public static bool operator ==(ShortcutBinding? left, ShortcutBinding? right) => Equals(left, right);
    public static bool operator !=(ShortcutBinding? left, ShortcutBinding? right) => !Equals(left, right);
}

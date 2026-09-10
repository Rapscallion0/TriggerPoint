using System;
using System.Collections.Generic;

namespace TriggerPoint.Core.Models;

public enum BrowserKind
{
    Default,
    Chromium,
    Firefox,
    Other
}

public class BrowserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public BrowserKind Kind { get; set; } = BrowserKind.Other;
    public List<BrowserProfileInfo> Profiles { get; set; } = [];

    public override string ToString() => Name;
}

public class BrowserProfileInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

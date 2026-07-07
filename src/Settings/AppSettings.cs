namespace NodeKit.Settings;

/// <summary>Connection settings persisted across NodeKit sessions.</summary>
internal sealed class AppSettings
{
    /// <summary>
    /// NodeVault gRPC server address (Build + Policy RPCs). Empty by default —
    /// every call site treats an empty address as "not configured yet" and
    /// prompts the user via the settings panel instead of connecting anywhere,
    /// so shipping a lab-specific address here would mean accidental submits
    /// to a specific internal host for anyone who never opens settings.
    /// </summary>
    public string NodeVaultAddress { get; set; } = string.Empty;

    /// <summary>NodePalette (Catalog) REST base URL (tool / data list queries). Empty by default; see <see cref="NodeVaultAddress"/>.</summary>
    public string CatalogAddress { get; set; } = string.Empty;
}

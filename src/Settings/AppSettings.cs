namespace NodeKit.Settings;

/// <summary>Connection settings persisted across NodeKit sessions.</summary>
public sealed class AppSettings
{
    /// <summary>NodeVault gRPC server address (Build + Policy RPCs).</summary>
    public string NodeVaultAddress { get; set; } = "http://100.123.80.48:50051";

    /// <summary>NodePalette (Catalog) REST base URL (tool / data list queries).</summary>
    public string CatalogAddress { get; set; } = "http://100.123.80.48:8080";
}

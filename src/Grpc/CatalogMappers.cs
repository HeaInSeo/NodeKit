using System;

namespace NodeKit.Grpc
{
    internal static class CatalogMappers
    {
        public static RegisteredTool ToRegisteredTool(CatalogToolDto dto)
        {
            var label = dto.DisplayLabel;
            if (string.IsNullOrEmpty(label))
            {
                label = string.IsNullOrEmpty(dto.Version)
                    ? dto.ToolName
                    : $"{dto.ToolName} {dto.Version}";
            }

            return new RegisteredTool
            {
                CasHash = dto.CasHash,
                ToolName = dto.ToolName,
                Version = dto.Version,
                StableRef = dto.StableRef,
                ImageUri = dto.ImageUri,
                Digest = dto.Digest,
                DisplayLabel = label,
                DisplayCategory = dto.DisplayCategory ?? string.Empty,
                LifecyclePhase = dto.LifecyclePhase,
                IntegrityHealth = dto.IntegrityHealth,
                RegisteredAt = DateTimeOffset.FromUnixTimeSeconds(dto.RegisteredAt),
            };
        }

        public static RegisteredData ToRegisteredData(CatalogDataDto dto)
        {
            var label = dto.DisplayLabel;
            if (string.IsNullOrEmpty(label))
            {
                label = string.IsNullOrEmpty(dto.Version)
                    ? dto.DataName
                    : $"{dto.DataName} {dto.Version}";
            }

            return new RegisteredData
            {
                CasHash = dto.CasHash,
                DataName = dto.DataName,
                Version = dto.Version,
                StableRef = dto.StableRef,
                Description = dto.Description ?? string.Empty,
                Format = dto.Format ?? string.Empty,
                SourceUri = dto.SourceUri ?? string.Empty,
                Checksum = dto.Checksum ?? string.Empty,
                StorageUri = dto.StorageUri ?? string.Empty,
                DisplayLabel = label,
                DisplayCategory = dto.DisplayCategory ?? string.Empty,
                LifecyclePhase = dto.LifecyclePhase,
                IntegrityHealth = dto.IntegrityHealth,
                RegisteredAt = DateTimeOffset.FromUnixTimeSeconds(dto.RegisteredAt),
            };
        }
    }
}

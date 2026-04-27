using System;
using NodeKit.Authoring;

namespace NodeKit.Grpc
{
    /// <summary>
    /// DataDefinition → DataRegisterRequest 변환.
    /// L1 검증 통과 후 호출된다.
    /// </summary>
    public static class DataRegisterRequestFactory
    {
        /// <summary>
        /// DataDefinition 초안으로부터 NodeVault 전송용 DataRegisterRequest를 생성한다.
        /// </summary>
        public static DataRegisterRequest FromDataDefinition(DataDefinition def)
        {
            ArgumentNullException.ThrowIfNull(def);
            return new DataRegisterRequest
            {
                DataDefinitionId  = def.Id,
                DataName          = def.Name,
                Version           = def.Version,
                Description       = def.Description,
                Format            = def.Format,
                SourceUri         = def.SourceUri,
                Checksum          = def.Checksum,
                DisplayLabel      = def.DisplayLabel,
                DisplayDescription = def.DisplayDescription,
                DisplayCategory   = def.DisplayCategory,
                DisplayTags       = new(def.DisplayTags),
            };
        }
    }
}

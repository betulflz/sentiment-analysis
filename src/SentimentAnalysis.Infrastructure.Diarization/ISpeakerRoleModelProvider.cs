namespace SentimentAnalysis.Infrastructure.Diarization;

/// <summary>Canlı "Yöneten / Karşı taraf" ayrımının embedding modelinin yolunu çözer (12). Diarization'ın
/// embedding modelinden AYRIDIR: kümeleme eşiği resnet34'e göre ölçüldü, tanıtma eşiği TitaNet-small'a göre
/// (12a). Dosya yoksa aksiyon-alınabilir <see cref="System.IO.FileNotFoundException"/>.</summary>
public interface ISpeakerRoleModelProvider
{
    string RoleEmbeddingModelPath { get; }
}

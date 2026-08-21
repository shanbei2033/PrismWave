using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Contracts;

public interface ITrackMetadataService
{
    /// <summary>读取音频文件标签。读失败时返回基础模型（字段为空、IsWritable 按扩展名判定）。</summary>
    Task<TrackMetadataModel> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>将编辑后的标签写回文件（仅标签层，不动音频流）。任何失败以返回值表达，不抛出。</summary>
    Task<TrackMetadataSaveResult> SaveAsync(
        string path,
        TrackMetadataModel metadata,
        string? newCoverImagePath = null,
        bool removeCover = false,
        CancellationToken cancellationToken = default);
}

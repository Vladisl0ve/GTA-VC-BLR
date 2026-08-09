using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface ITxdReader
{
    TxdDocument Read(ReadOnlyMemory<byte> data, string sourceName);
}

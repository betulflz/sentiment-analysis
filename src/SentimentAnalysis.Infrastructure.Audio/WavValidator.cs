using System;
using System.IO;

namespace SentimentAnalysis.Infrastructure.Audio;

public static class WavValidator
{
    public static void ValidateWav(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"WAV dosyası bulunamadı: {path}", path);

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length < 44)
            throw new InvalidOperationException("WAV dosyası geçerli bir başlık içermek için çok küçük.");

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs);

        // 1. RIFF
        var riff = reader.ReadChars(4);
        if (new string(riff) != "RIFF")
            throw new InvalidOperationException("Geçersiz WAV formatı: 'RIFF' imzası bulunamadı.");

        int fileSize = reader.ReadInt32();

        // 2. WAVE
        var wave = reader.ReadChars(4);
        if (new string(wave) != "WAVE")
            throw new InvalidOperationException("Geçersiz WAV formatı: 'WAVE' imzası bulunamadı.");

        // 3. fmt chunk
        var fmt = reader.ReadChars(4);
        if (new string(fmt) != "fmt ")
            throw new InvalidOperationException("Geçersiz WAV formatı: 'fmt ' chunk'ı bulunamadı.");

        int fmtSize = reader.ReadInt32();
        
        // 4. PCM format code 1
        short formatCode = reader.ReadInt16();
        if (formatCode != 1)
            throw new InvalidOperationException($"Desteklenmeyen WAV format kodu: {formatCode}. Yalnızca PCM (1) desteklenir.");

        // 5. Channel count 1
        short channels = reader.ReadInt16();
        if (channels != 1)
            throw new InvalidOperationException($"Desteklenmeyen kanal sayısı: {channels}. Yalnızca Mono (1) desteklenir.");

        // 6. Sample rate 16000
        int sampleRate = reader.ReadInt32();
        if (sampleRate != 16000)
            throw new InvalidOperationException($"Desteklenmeyen örnekleme hızı: {sampleRate} Hz. Yalnızca 16000 Hz desteklenir.");

        int byteRate = reader.ReadInt32();
        short blockAlign = reader.ReadInt16();

        // 7. Bits per sample 16
        short bitsPerSample = reader.ReadInt16();
        if (bitsPerSample != 16)
            throw new InvalidOperationException($"Desteklenmeyen bit derinliği: {bitsPerSample}. Yalnızca 16-bit desteklenir.");

        // Read remaining fmt bytes if fmtSize > 16
        if (fmtSize > 16)
        {
            reader.ReadBytes(fmtSize - 16);
        }

        // 8. Find data chunk
        bool dataFound = false;
        int dataSize = 0;
        
        while (fs.Position < fs.Length - 8)
        {
            var chunkId = new string(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            if (chunkId == "data")
            {
                dataFound = true;
                dataSize = chunkSize;
                break;
            }
            else
            {
                if (chunkSize < 0 || fs.Position + chunkSize > fs.Length)
                {
                    break; // invalid chunk size
                }
                fs.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        if (!dataFound)
            throw new InvalidOperationException("Geçersiz WAV formatı: 'data' chunk'ı bulunamadı.");

        // 9. data size > 0
        if (dataSize <= 0)
            throw new InvalidOperationException("Ses dosyasında analiz edilebilecek bir ses algılanamadı (data boyutu 0).");
    }
}

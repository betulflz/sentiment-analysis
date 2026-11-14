using System;

namespace SentimentAnalysis.Core;

/// <summary>Bellekteki tek bir görüntü karesi (RGB24, satır satır). Diske YAZILMAZ (spec 11 §4).
/// <paramref name="Time"/> oturum başından itibaren — ses chunk'larıyla aynı eksen.</summary>
public sealed record VideoFrame(byte[] Rgb24, int Width, int Height, TimeSpan Time);

/// <summary>Kare pikselinde yüz kutusu (sol-üst köşe + boyut).</summary>
public readonly record struct FaceBox(float X, float Y, float W, float H)
{
    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;
}

/// <summary>Yüz algılayıcının çıktısı. <paramref name="Kps"/>: 10 değer — sağ göz, sol göz, burun,
/// ağız sağ, ağız sol (x,y sırasıyla), kare pikselinde.</summary>
public sealed record DetectedFace(FaceBox Box, float Score, float[] Kps);

/// <summary>Takip edilen bir yüzün bir karedeki gözlemi. <paramref name="Expression"/>: FER+ sırasıyla
/// 8 olasılık (nötr, mutlu, şaşkın, üzgün, kızgın, iğrenmiş, korkmuş, küçümseyen).</summary>
public sealed record FaceObservation(int FaceIndex, TimeSpan Time, FaceBox Box, double Yaw, float[] Expression);

# M1 Air kalite pilotu uygulama planı

**Amaç:** 8 GB M1 Air üzerinde mevcut motoru ve alternatifleri aynı Türkçe kayıtlarda, kayıt kaybetmeden, süre/bellek/doğruluk ile karşılaştırmak.

**Dayanak:** `docs/research/2026-11-13-kalite-ve-acik-kaynak-arastirmasi.md`; kullanıcının yerel M1 Air kısıtı ve ilerleme onayı.

**Sınırlar:** Model indirme veya bulut servisi yok. Aynı anda tek kayıt/tek motor. Ölçüm araçları model kurulu olmadan test edilebilir. Özel kayıtlar ve sonuç metinleri git'e eklenmez. Duygu güveni/öneri ağırlıkları, gerçek veri ölçülmeden değiştirilmez.

- [x] Konuşmacı ataması: `SpeakerAlignerTests.cs` içinde parçalanmış turlar, yinelenen/örtüşen aynı kişi bölgeleri, beraberlik ve Unknown için regresyon testleri yaz; eski kodda beklenen hataları gör. `BestSpeaker` içinde kişi başına birleşim süresini hesapla; eşitlikte ilk kişi sırasını koru. İki kişi eşit örtüşüyorsa belirsiz işaretle. İlgili testleri çalıştır.
- [x] `tools/quality/benchmark.py`: JSON manifest ve motor çıktısı doğrulama; Türkçe WER/CER ve optimal anonim kimlik eşlemesiyle collar=0, overlap dahil DER. Önce `test_benchmark.py` ile bilinen hata, etiket permütasyonu, örtüşme, eksik kayıt, geçersiz zaman ve Türkçe normalizasyon testleri yaz. Python standart kütüphanesi; model bağımlılığı yok.
- [x] Aynı araca sıralı `run` ekle: JSON argv ile shell kullanmadan motor çağır; kayıt başına timeout, çıkış kodu, wall-time ve macOS `/usr/bin/time -l` tepe RSS ölç; hatalı çıktıyı skorlamayı reddet. Sonuçlar ayrı kullanıcı klasörüne yazılır. Başarısız koşu başarı gibi raporlanmaz.
- [x] `tools/SentimentAnalysis.Benchmark`: mevcut Whisper ve sherpa için net9.0 CLI. `transcribe` ve `diarize` ayrı komutlar; gerekli model dosyaları koşudan önce doğrulanır. WAV girdisi; çıktı ortak JSON sözleşmesi. Duygu/UI modelleri yüklenmez. Offline tüm parçalar sırayla işlenir; bounded canlı pipeline kullanılıp parça düşürülmez.
- [x] Kullanım belgesi, örnek manifest ve sahte veri olduğu açık olan skor örneği ekle. Gerçek veri ve motor revizyonlarını raporda ayır. M1 için soğuk süreç ve ses süresine göre işlem oranı açıklanır.
- [x] Python testleri, .NET testleri, çözüm derlemesi; örnek skor ve eksik model koşusunu çalıştır. Kod incelemesi al ve önemli bulguları düzelt. Gerçek model ölçümü için kullanıcı dosyaları mevcut değilse bunu açıkça belirt.

**Sonraki aşamalar:** Aynı etiketli Türkçe pilot üzerinde mevcut sherpa/Community-1/Nemotron ve transkripsiyon adayları. Hız/bellek bütçesini geçenler elenir. Yerel ONNX kaynak revizyonu ve referans logitleri doğrulandıktan sonra çok etiketli metin işleme; ardından duygu ve öneri değerlendirmesi.

**Doğrulama sonucu:** 834 .NET testi geçti, 16 model/ortam testi atlandı; 14 Python testi geçti. Çözüm derlemesi 0 hata/0 uyarı. CLI eksik modelde çıkış 2 veriyor ve çıktı üretmiyor. Sentetik skor örneği beklenen WER=0,25 değerini verdi; gerçek model ölçümü değildir. Kod incelemesinde bulunan Ctrl+C süreç sızıntısı testle yeniden üretildi ve düzeltildi. Gerçek Türkçe kayıtlar ve model dosyaları olmadan doğruluk/hız iyileşmesi iddia edilmiyor.

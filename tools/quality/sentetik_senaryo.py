"""İt.4 sentetik olay senaryoları (macOS `say` + ffmpeg). Çıktılar çalışılan klasöre yazılır.
Kullanım: python3 tools/quality/sentetik_senaryo.py [senaryo-adı ...]   — bkz. docs/research/2026-11-14-sentetik-olay-senaryolari.md"""
import subprocess, wave, json, os, sys
SC = {
 "s1-cevapsiz-soru": [
  ("A","Merhaba, bugün yeni yazılım paketimizi konuşmak için toplandık. Sizin ekibinizin ihtiyaçlarını dinlemek istiyorum.",0.7),
  ("B","Merhaba. Biz şu anda müşteri kayıtlarını elle tutuyoruz ve bu çok zaman alıyor.",0.7),
  ("A","Anlıyorum, bizim paketimiz bu kayıtları otomatik olarak düzenliyor ve raporlar üretiyor.",0.7),
  ("B","Peki bu paketin yıllık lisans ücreti tam olarak ne kadar olacak?",0.7),
  ("B","Ayrıca kurulum için ekibimizden kaç kişinin zaman ayırması gerekiyor, onu da merak ediyorum.",1.0),
  ("A","Paketin arayüzü çok sade, ekranlar renkli ve kullanımı oldukça kolay. Mobil uygulaması da var, telefondan da girebiliyorsunuz. Raporları da istediğiniz biçimde dışa aktarabiliyorsunuz. Birçok firma bu kolaylıktan çok memnun kaldı.",0.7),
  ("A","Ayrıca destek ekibimiz hafta içi her gün çalışıyor ve sorularınıza hızlıca yanıt veriyor. Eğitim videolarımız da oldukça kapsamlı.",0.7),
  ("B","Tamam, anlaşıldı. Haftaya salı size dönüş yapacağım, teklifi e-posta ile gönderirseniz inceleriz.",0.7),
  ("A","Elbette, yarın sabah teklifi e-posta ile göndereceğim. Teşekkür ederim.",0.5)],
 "s2-uzun-tek-kisi": [
  ("A","Hoş geldiniz. Bugün projenin ilk üç ayını değerlendireceğiz. İzninizle önce genel bir özet vereyim.",0.6),
  ("A","İlk ay ekibi kurduk ve gereksinimleri topladık. Müşteriyle dört ayrı toplantı yaptık ve her birinde farklı beklentiler ortaya çıktı. Bu beklentileri tek bir belgede birleştirmek sandığımızdan uzun sürdü.",0.4),
  ("A","İkinci ay tasarım aşamasına geçtik. Veritabanı yapısını iki kez değiştirmek zorunda kaldık, çünkü raporlama ihtiyaçları ilk tasarıma uymuyordu. Bu da yaklaşık iki haftalık bir kayba yol açtı.",0.4),
  ("A","Üçüncü ay geliştirmeye başladık. Kullanıcı girişi, kayıt ekranları ve temel raporlar tamamlandı. Test ekibi şu ana kadar otuz iki hata buldu, bunların yirmisi kapatıldı.",0.4),
  ("A","Bütçe tarafında planın yüzde on kadar üzerindeyiz. Bunun ana nedeni tasarım değişikliği ve dış danışmanlık giderleri oldu. Önümüzdeki dönemde bu farkı kapatmak için bazı kalemleri erteleyebiliriz.",0.4),
  ("A","Ekip motivasyonu genel olarak iyi, ancak iki arkadaşımız fazla mesaiden şikayetçi. Bu konuyu insan kaynaklarıyla da konuştum. Belki geçici olarak bir kişi daha eklememiz gerekebilir.",0.4),
  ("A","Müşteri tarafında da memnuniyet fena değil. Son toplantıda ekranları gösterdik, genel olarak beğendiler ama raporların biraz daha sade olmasını istediler. Bu değişiklik küçük, bir haftada yapılabilir.",0.4),
  ("A","Riskler konusunda ise en büyük endişem entegrasyon. Müşterinin eski sistemiyle veri alışverişi henüz denenmedi ve orada sürprizler çıkabilir. Bunu erken denemek istiyorum.",0.8),
  ("B","Peki entegrasyon testini ne zaman yapmayı planlıyorsunuz?",0.6),
  ("A","Önümüzdeki hafta çarşamba günü ilk denemeyi yapacağız ve sonuçları cuma günü size e-posta ile göndereceğim.",0.5)],
 "s3-belirsiz-kapanis": [
  ("A","Merhaba, görüşmeye katıldığınız için teşekkürler. Bugün mevcut tedarikçinizle yaşadığınız sorunları konuşmak istiyorum.",0.7),
  ("B","Merhaba. Asıl sorunumuz teslimatların sürekli gecikmesi. Geçen ay üç kez geciktiler.",0.7),
  ("A","Bu gerçekten can sıkıcı. Bizim depomuz şehrin içinde, bu yüzden teslimat süremiz genelde iki gün.",0.7),
  ("B","İki gün bizim için yeterli olur. Fiyatlarınız nasıl peki?",0.7),
  ("A","Fiyatlarımız piyasa ortalamasında, büyük siparişlerde indirim de uyguluyoruz.",0.7),
  ("B","Anladım. Bunu ekipte bir konuşalım.",0.7),
  ("A","Tabii, nasıl isterseniz. Konuştuğumuza sevindim.",0.7),
  ("B","Ben de öyle. İyi günler dilerim.",0.6),
  ("A","Size de iyi günler.",0.5)],
 "s5-gercek-cevapsiz": [
  ("A","Merhaba, sözleşmenin yenilenmesini konuşmak için buradayız.",0.7),
  ("B","Merhaba. Biz de yenilemeyi düşünüyoruz ama bazı koşulları tekrar görmek istiyoruz.",0.7),
  ("A","Tabii. Yeni sözleşmeyi hangi tarihte imzalamayı düşünüyorsunuz?",1.2),
  ("A","Bu arada geçen yıl sizinle yaptığımız proje bizim için çok öğretici oldu. Ekibimiz sizin süreçlerinizden çok şey öğrendi ve bunu başka müşterilerimize de anlatıyoruz. Özellikle raporlama tarafındaki düzeniniz gerçekten örnek alınacak cinsten.",0.7),
  ("B","Teşekkürler, ekibimiz de sizinle çalışmaktan memnundu.",0.7),
  ("A","O zaman yarın size yeni sözleşme taslağını e-posta ile göndereceğim.",0.5)],
 "s4-kontrol": [
  ("A","Merhaba, bugün yeni ofis taşınmasını planlamak için buradayız.",0.7),
  ("B","Merhaba. Taşınma tarihini kesinleştirdiniz mi?",0.7),
  ("A","Evet, taşınma ayın on beşinde olacak. Nakliye firmasıyla anlaştık.",0.7),
  ("B","Güzel. Bilgisayarların kurulumu kimin sorumluluğunda?",0.7),
  ("A","Bilgi işlem ekibimiz kurulumu yapacak, iki gün sürmesini bekliyoruz.",0.7),
  ("B","Peki çalışanlara ne zaman bilgi vereceğiz?",0.7),
  ("A","Pazartesi günü herkese e-posta göndereceğim, ayrıntılı planı da ekleyeceğim.",0.7),
  ("B","Harika. O zaman perşembe günü son durumu tekrar konuşalım.",0.7),
  ("A","Anlaştık, perşembe saat onda görüşürüz.",0.5)],
}
def run(c): subprocess.run(c, check=True, capture_output=True)
for name, turns in SC.items():
    if len(__import__("sys").argv)>1 and name not in __import__("sys").argv[1:]: continue
    os.makedirs(name, exist_ok=True); pcm=b""; t=0.0; ref=[]
    for k,(sp,txt,gap) in enumerate(turns):
        a=f"{name}/t{k}.aiff"; w=f"{name}/t{k}.wav"
        run(["say","-v","Yelda","-r","175","-o",a,txt])
        af="aresample=16000" if sp=="A" else "asetrate=22050*0.78,aresample=16000,atempo=1.282"
        run(["ffmpeg","-y","-loglevel","error","-i",a,"-af",af,"-ac","1","-ar","16000","-sample_fmt","s16",w])
        r=wave.open(w); fr=r.readframes(r.getnframes()); d=r.getnframes()/16000
        ref.append({"speaker":"0" if sp=="A" else "1","start":round(t,2),"end":round(t+d,2),"text":txt})
        pcm+=fr+b"\0\0"*int(gap*16000); t+=d+gap
    o=wave.open(f"{name}.wav","wb"); o.setnchannels(1); o.setsampwidth(2); o.setframerate(16000); o.writeframes(pcm); o.close()
    json.dump({"speakers":ref},open(f"{name}-ref.json","w"),ensure_ascii=False)
    print(name, round(t,1),"sn")

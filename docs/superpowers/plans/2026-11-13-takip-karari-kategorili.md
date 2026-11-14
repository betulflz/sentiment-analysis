# Takip kararı — kategorili ekran: uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Görüşme sonu karar kartında her profil için 6–8 yumuşak dilli takip seçeneği, 4 kategori, "önerilen yön + ilk 3 seçenek + neden" gösterimi.

**Architecture:** Seçenekler/kategoriler/etki puanları `DecisionKnowledgeBase`'te; `FollowUpAdvisor` aynı TOPSIS motoruyla sıralar ve kararın yönünü (1. seçeneğin kategorisi) ile gösterilecek ilk 3 seçeneği döndürür; `FollowUpPanelViewModel` bunları metne çevirir, `ReportView.axaml` kartı gösterir.

**Tech Stack:** .NET 9, xUnit, Avalonia, CommunityToolkit.Mvvm.

**Spec:** `docs/superpowers/specs/2026-11-13-takip-karari-kategorili-design.md`

## Global Constraints

- `FollowUpAction` SQLite'ta `int` olarak saklanır: mevcut 10 değerin sırası/sayısı DEĞİŞMEZ, hiçbiri silinmez; yeniler **sona** eklenir.
- Hiçbir metin kesin "işe al / reddet / satın almaz" hükmü vermez; maaş, dil, bütçe, teknik yeterlilik seçeneği yok.
- Kategori adları ekranda tam olarak: **İlerletin**, **Netleştirin**, **Bekleyin**, **İlişkiyi gözetin**.
- Ekran notu tam metin: `Bu bir öneridir; kararı siz verirsiniz. Sistem yalnız ses tonu, yüz ifadesi ve konuşma payını ölçer.`
- Yeni etki puanları geliştirici tahminidir; `FollowUpImpact` yorumunda "KALİBRE EDİLMEMİŞ" notu korunur.
- Veritabanı şeması, kaydırıcılar, duyarlılık analizi, canlı öneriler (LiveAction) değişmez.
- Her görev sonunda kullanıcı onayı alınmadan commit edilmez ve sonraki göreve geçilmez.

## Review Focus

1. **Eski kayıtlı rapor açılması:** veritabanındaki eski `FollowUp` sayısı (0–9) aynı seçeneğe karşılık gelmeli → Task 1'deki sayısal sabitlik testi.
2. **Kural yolunda kaydırıcı oynatma:** sakin görüşmede ağırlık değişince yön "İlerletin" kalmalı, "İlk 3" puansız listelenmeli, çökme olmamalı → Task 3 testi.
3. **Toplantının tek İlerletin seçeneği:** sakin toplantıda "İlk 3" listesi 1 satır olmalı (boş satır/indeks hatası yok) → Task 2 testi.
4. **Etiketi unutulmuş yeni seçenek:** ekranda enum adı (`ReferansKontrolu`) görünmemeli → Task 1'deki "her seçeneğin Türkçe adı var" testi.
5. **Karar verisi olmayan oturum:** `Load` boş oturumda yön/ilk 3 metinleri temizlenmeli → Task 3 testi.

---

### Task 1: Seçenekler, kategoriler, etiketler, etki puanları

**Files:**
- Modify: `src/SentimentAnalysis.Core/Decision.cs:47-52` (enum'a ekle, yeni enum)
- Modify: `src/SentimentAnalysis.Application/Decision/DecisionKnowledgeBase.cs` (ProfileDefinition, FollowUpImpact, Profiles, CategoryOf)
- Modify: `src/SentimentAnalysis.Application/Decision/DecisionText.cs:65-77` (Name(FollowUpAction), yeni Name(FollowUpCategory))
- Test: `tests/SentimentAnalysis.Application.Tests/Decision/DecisionKnowledgeBaseTests.cs`

**Interfaces — Produces:**
- `FollowUpAction` sonuna, bu sırayla: `ReferansKontrolu, DemoOner, IkinciGorusAl, NetlestirmeGorusmesi, TeknikDemo, CalismaGrubu, YaziliGorus, AdaylarlaKarsilastir, BilgiListesi, KarariErtele, IliskiyiOnar`
- `public enum FollowUpCategory { Ilerletin, Netlestirin, Bekleyin, IliskiyiGozetin }` (Core/Decision.cs)
- `ProfileDefinition(..., IReadOnlyList<FollowUpAction> FollowUps, FollowUpAction PositiveFollowUp, IReadOnlyList<FollowUpAction> AdvanceOptions)` — `AdvanceOptions[0] == PositiveFollowUp`
- `public static FollowUpCategory DecisionKnowledgeBase.CategoryOf(FollowUpAction a)`
- `public static string DecisionText.Name(FollowUpCategory c)` → "İlerletin" / "Netleştirin" / "Bekleyin" / "İlişkiyi gözetin"

**Profil listeleri (spec tablosundan):**

| Profil | AdvanceOptions (İlerletin) | FollowUps (TOPSIS ile sıralanan) |
|---|---|---|
| Mulakat | DegerlendirmeyeGec, ReferansKontrolu | EkGorusme, IkinciGorusAl, NetlestirmeGorusmesi, AdaylarlaKarsilastir, GorusmeyiTekrarla |
| Satis | TeklifGonder, DemoOner | ItirazBilgisi, IkinciGorusme, TeknikDemo, Beklet, BilgiListesi, IliskiyiOnar |
| Toplanti | KararEpostasi | TakipToplantisi, CalismaGrubu, YaziliGorus, KarariErtele, BireBirGorusme |

**Kategoriler:** İlerletin = DegerlendirmeyeGec, ReferansKontrolu, TeklifGonder, DemoOner, KararEpostasi · Netleştirin = EkGorusme, IkinciGorusAl, NetlestirmeGorusmesi, ItirazBilgisi, IkinciGorusme, TeknikDemo, TakipToplantisi, CalismaGrubu, YaziliGorus · Bekleyin = AdaylarlaKarsilastir, Beklet, BilgiListesi, KarariErtele · İlişkiyi gözetin = GorusmeyiTekrarla, IliskiyiOnar, BireBirGorusme.

**Etiketler (DecisionText.Name), tam metin:**
DegerlendirmeyeGec "Bir sonraki aşamaya davet etmeyi değerlendirin" · ReferansKontrolu "Referans kontrolüne geçmeyi düşünün" · EkGorusme "Ek teknik değerlendirme veya örnek ödev isteyin" · IkinciGorusAl "İK'dan ya da başka bir uzmandan ikinci görüş alın" · NetlestirmeGorusmesi "Netleşmeyen konular için kısa bir ikinci görüşme yapın" · AdaylarlaKarsilastir "Diğer adaylarla karşılaştırıp kararı sonra verin" · GorusmeyiTekrarla "Görüşmeyi daha rahat bir ortamda tekrarlayın" · TeklifGonder "Teklif veya fiyat önerisi gönderin" · DemoOner "Deneme sürümü ya da demo önerin" · ItirazBilgisi "İtirazları yanıtlayan bilgi ve örnekler gönderin" · IkinciGorusme "Karar vericiyle ikinci görüşme ayarlayın" · TeknikDemo "Teknik ekiple ayrıntılı demo planlayın" · Beklet "Acele etmeyin, bir hafta sonra kısa bir hatırlatma yapın" · BilgiListesi "Bilgilendirme listesine alın" · IliskiyiOnar "Gergin geçti: önce kısa bir telefonla ilişkiyi onarın" · KararEpostasi "Kararları ve sorumluları yazan özet e-postası gönderin" · TakipToplantisi "Takip toplantısı planlayın" · CalismaGrubu "Açık konular için küçük bir çalışma grubu kurun" · YaziliGorus "Az konuşanlardan yazılı görüş isteyin" · KarariErtele "Kararı erteleyin, eksik bilgiyi toplayın" · BireBirGorusme "Gerginlik yaşanan kişiyle birebir görüşün".

**Başlangıç etki puanları** (kriter sırası Gerilim, İlgiKaybı, OlumsuzDuygu, Uyumsuzluk, DüşükEnerji, Kötüleşme; Task 2 senaryolarını sağlamak için ayarlanabilir; mevcut satırlar yalnız gerekirse değişir):
ReferansKontrolu {.2,.3,.3,.5,.3,.3} · DemoOner {.2,.7,.3,.4,.6,.3} · IkinciGorusAl {.3,.3,.8,.6,.3,.5} · NetlestirmeGorusmesi {.4,.5,.4,.7,.4,.4} · TeknikDemo {.2,.6,.3,.7,.6,.3} · CalismaGrubu {.2,.6,.3,.6,.5,.4} · YaziliGorus {.1,.6,.2,.4,.9,.3} · AdaylarlaKarsilastir {.4,.3,.4,.4,.3,.8} · BilgiListesi {.2,.6,.2,.2,.6,.4} · KarariErtele {.4,.3,.4,.6,.3,.8} · IliskiyiOnar {.9,.3,.8,.4,.2,.7}. (İlerletin seçeneklerinin puanı TOPSIS'te kullanılmaz ama tablo bütünlüğü için tutulur.)

- [ ] **Step 1: Failing testler** (`DecisionKnowledgeBaseTests.cs`)
  - `FollowUpAction_EskiDegerlerinSayisiSabit`: `TeklifGonder==0, IkinciGorusme==1, ItirazBilgisi==2, Beklet==3, DegerlendirmeyeGec==4, EkGorusme==5, GorusmeyiTekrarla==6, KararEpostasi==7, BireBirGorusme==8, TakipToplantisi==9` (`(int)` ile).
  - `HerTakipSeceneginin_TurkceAdiVeKategorisiVar`: her `Enum.GetValues<FollowUpAction>()` için `DecisionText.Name(a) != a.ToString()` ve `CategoryOf(a)` istisna atmaz.
  - `HerTakipSeceneginin_EtkiSatiriVar`: her enum değeri `FollowUpImpact`'te.
  - `AdvanceOptions_IlerletinVeIlkiPozitif` (Theory, 3 profil): `AdvanceOptions[0]==PositiveFollowUp`, tümü `Ilerletin`; `FollowUps`'ta hiç `Ilerletin` yok; iki liste kesişmez.
  - `ProfilListeleri_SpecTablosu`: yukarıdaki tablo sırasıyla `Assert.Equal`.
- [ ] **Step 2:** `dotnet test tests/SentimentAnalysis.Application.Tests --filter DecisionKnowledgeBaseTests` → derleme hatası/FAIL.
- [ ] **Step 3:** Enum'ları, `ProfileDefinition.AdvanceOptions`, `CategoryOf` (switch, eksik değerde `ArgumentOutOfRangeException`), etiketleri ve etki satırlarını ekle.
- [ ] **Step 4:** Aynı komut → PASS; ardından `dotnet test SentimentAnalysis.sln` (FollowUpAdvisorTests'teki senaryo kırılmaları Task 2'de ele alınır — burada kırılan test varsa listele, düzeltme Task 2'de).
- [ ] **Step 5:** Kullanıcıya göster, onayla commit: `feat(takip): kategorili takip seçenekleri ve etiketler`.

### Task 2: Danışman — yön ve ilk 3 seçenek

**Files:**
- Modify: `src/SentimentAnalysis.Core/Decision.cs:126` (FollowUpDecision)
- Modify: `src/SentimentAnalysis.Application/Decision/FollowUpAdvisor.cs:34-48`
- Test: `tests/SentimentAnalysis.Application.Tests/Decision/FollowUpAdvisorTests.cs`

**Interfaces:**
- Consumes: Task 1 `CategoryOf`, `AdvanceOptions`.
- Produces: `public sealed record FollowUpDecision(FollowUpAction Best, IReadOnlyList<Ranked<FollowUpAction>> Ranking, string Reason, FollowUpCategory Direction, IReadOnlyList<FollowUpAction> Options);` — `Options.Count` 1–3, `Options[0]==Best`.

**Davranış:**
- Düşük kalite (Mülakat): Best=GorusmeyiTekrarla, Direction=IliskiyiGozetin, Options=[GorusmeyiTekrarla], Ranking boş (gerekçe metni aynen).
- Sakin: Best=PositiveFollowUp, Direction=Ilerletin, Options=`AdvanceOptions.Take(3)`, Ranking boş.
- Diğer: Ranking=TOPSIS(`FollowUps`), Best=Ranking[0], Direction=`CategoryOf(Best)`, Options=Ranking ilk 3'ün seçenekleri.

**Beklenen senaryolar** (Task 1 puanları bunları sağlayacak şekilde ayarlanır; `Satis_Gergin_*` closeness sayısı yeni değere güncellenir, kazanan değil):

| Test | Profil | Göstergeler | Best | Direction |
|---|---|---|---|---|
| Satis_Gergin_IliskiyiOnar | Satis | Gergin {.8,.1,.7,.2,.1,.3} | IliskiyiOnar | IliskiyiGozetin |
| Satis_Ilgisiz_IkinciGorusme | Satis | Ilgisiz {.1,.9,.1,.1,.6,.2} | IkinciGorusme | Netlestirin |
| Satis_Uyumsuz_ItirazBilgisi | Satis | Uyumsuz {.2,.1,.2,.9,.1,.1} | ItirazBilgisi | Netlestirin |
| Satis_Kotulesen_Beklet | Satis | {.3,.2,.4,.2,.2,.9} | Beklet | Bekleyin |
| Mulakat_Uyumsuz_EkGorusme | Mulakat | Uyumsuz | EkGorusme | Netlestirin |
| Mulakat_Gergin_Tekrarla | Mulakat | Gergin | GorusmeyiTekrarla | IliskiyiGozetin |
| Mulakat_Olumsuz_IkinciGorusAl | Mulakat | {.2,.1,.9,.3,.1,.3} | IkinciGorusAl | Netlestirin |
| Mulakat_Kotulesen_Karsilastir | Mulakat | {.3,.2,.4,.2,.2,.9} | AdaylarlaKarsilastir | Bekleyin |
| Toplanti_Gergin_BireBir | Toplanti | Gergin | BireBirGorusme | IliskiyiGozetin |
| Toplanti_Ilgisiz_Takip | Toplanti | Ilgisiz | TakipToplantisi | Netlestirin |
| Toplanti_DusukEnerji_YaziliGorus | Toplanti | {.1,.3,.1,.1,.9,.2} | YaziliGorus | Netlestirin |
| Toplanti_Kotulesen_Ertele | Toplanti | {.3,.2,.4,.2,.2,.9} | KarariErtele | Bekleyin |

- [ ] **Step 1: Failing testler:** yukarıdaki 12 senaryo (eski adı değişenleri yeniden adlandır); `Calm_PositiveFollowUp` Theory'sine `Direction==Ilerletin` ve `Options==AdvanceOptions.Take(3)` ekle; `Calm_Toplanti_TekSecenek` (`Options.Count==1`); `Mulakat_LowQuality_Repeat`'e `Direction==IliskiyiGozetin`, `Options==[GorusmeyiTekrarla]`; `Topsis_OptionsIlkUcVeIlkiBest` (Gergin Satış: `Options.Count==3`, `Options[0]==Best`, `Options`==Ranking.Take(3) seçenekleri); `Topsis_IlerletinSiralanmaz` (hiçbir senaryonun Ranking'inde Ilerletin yok).
- [ ] **Step 2:** `dotnet test tests/SentimentAnalysis.Application.Tests --filter FollowUpAdvisorTests` → FAIL.
- [ ] **Step 3:** `FollowUpDecision`'a iki alan ekle, `Decide`'ı yukarıdaki davranışa göre güncelle; senaryolar tutmuyorsa yalnız Task 1'in YENİ satırlarındaki puanları ayarla (kısa bir Python/C# denemesiyle), mevcut satırlara dokunmamaya çalış.
- [ ] **Step 4:** Filtreli test → PASS; `dotnet test SentimentAnalysis.sln` → 0 başarısız (Desktop derleme hatası çıkarsa Task 3'e bırakma: `FollowUpDecision` kurucusunu kullanan her yer burada derlenmeli).
- [ ] **Step 5:** Kullanıcı onayıyla commit: `feat(takip): danışman yön ve ilk üç seçeneği döndürür`.

### Task 3: Rapor kartı — yön, ilk 3, not

**Files:**
- Modify: `src/SentimentAnalysis.Desktop/FollowUpPanelViewModel.cs` (`OnWeightChanged`, `Load`)
- Modify: `src/SentimentAnalysis.Desktop/DecisionPanelViewModel.cs:268` yanına yeni record
- Modify: `src/SentimentAnalysis.Desktop.Avalonia/Views/ReportView.axaml:74-79`
- Test: `tests/SentimentAnalysis.Desktop.Tests/FollowUpPanelViewModelTests.cs`

**Interfaces:**
- Consumes: Task 2 `FollowUpDecision.Direction/Options/Ranking`.
- Produces: `[ObservableProperty] string DirectionText` ("Önerilen yön: Netleştirin"); `[ObservableProperty] IReadOnlyList<TopOptionRowVm> TopOptions`; `public sealed record TopOptionRowVm(string Label, string PercentText, string WhyText);` — TOPSIS yolunda `Label="{i+1}. {Name}"`, `PercentText="%71"`, `WhyText` = o satırın `TopCriteria` adları virgülle (`DecisionText.Name(Criterion)`); kural yolunda `PercentText=""`, `WhyText=""`.

- [ ] **Step 1: Failing testler:**
  - `Gergin_Satis_YonVeIlkUc`: `DirectionText=="Önerilen yön: İlişkiyi gözetin"`, `TopOptions.Count==3`, `TopOptions[0].Label` "1. Gergin geçti:" ile başlar, `PercentText` "%" ile başlar, `WhyText` boş değil.
  - `Sakin_YonIlerletin_PuansizListe`: `DirectionText=="Önerilen yön: İlerletin"`, tüm `PercentText==""`.
  - `Sakin_AgirlikDegisince_YonAyniKalir`: Sakin oturumda bir `Weights[i].Value` değişince `DirectionText` aynı, çökme yok.
  - `Bos_Oturum_YonVeIlkUcTemizlenir`: pencere olmayan oturumla `Load` → `DirectionText==""`, `TopOptions` boş.
- [ ] **Step 2:** `dotnet test tests/SentimentAnalysis.Desktop.Tests --filter FollowUpPanelViewModelTests` → FAIL.
- [ ] **Step 3:** VM alanlarını doldur; XAML'de başlığın altına `DirectionText` için mevcut `pill` sınıflı bir Border, `ReasonText`'in altına "İlk 3 seçenek" `sectionTitle` + `TopOptions` ItemsControl (Grid `*,48,Auto`: Label · PercentText · WhyText muted), ardından Global Constraints'teki not metni `muted` TextBlock olarak. Mevcut "Seçeneklerin uygunluk puanı" listesi yerinde kalır.
- [ ] **Step 4:** Filtreli test → PASS; `dotnet build SentimentAnalysis.sln` 0 uyarı/0 hata; `dotnet test SentimentAnalysis.sln` 0 başarısız (çıktıyı göster).
- [ ] **Step 5:** `./build-macos-app.sh` (önce `osascript -e 'quit app "Sesli Duygu Analizi"'`), kullanıcı canlı dener: üç profilde karar kartı yön + ilk 3 + not; kaydırıcıyla yön değişebiliyor. (Geçmiş ekranı eski raporu açmadığı için eski kayıt koruması yalnız Task 1 testiyle doğrulanır.) Onayla commit: `feat(takip): rapor kartında önerilen yön ve ilk üç seçenek`.

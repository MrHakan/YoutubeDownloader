# YoutubeDownloader geliştirme planı

Tarih: 10 Ekim 2026

Hedef: MrHakan'ın bağımsız, güvenilir ve kullanımı kolay YouTube indirme uygulaması.

## Uygulama durumu — 1.17.0

Beş aşamanın kod ve yerel paketleme işleri uygulandı. Aşağıdaki başlangıç durumu, plan hazırlanırken alınan kayıttır; teslimler tek bir 1.17.0 sürümünde birleştirildi.

- Kalıcı test projesi: Windows üzerinde 27 ağsız kontrol, arayüz kontrolü ve iki canlı entegrasyon kontrolü; toplam 30 kontrol geçti.
- Gerçek MP4/MP3 dosyaları FFmpeg ile çözümlendi; herkese açık playlist çözümlendi; aktarım sırasında iptal eski dosyayı korudu ve geçici dosyaları temizledi.
- Kuyruk, ayarlar, eski ayar geçişi, çerez şifreleme, yeniden deneme, çakışma ve dosya güvenliği senaryoları test edildi.
- FFmpeg içermeyen temiz kaynak kopyasından, boşluk içeren dizinde Windows x64 bağımsız paket üretildi. Paketin kendi çalışma zamanı ve FFmpeg'i ile kontroller çalıştırıldı.
- Türkçe/İngilizce ekranlar açıldı; koyu temada ana ekranın 200 DPI görüntüsü incelendi ve düğme okunabilirliği düzeltildi.
- Paket içerik kontrolü, VERSION.json ve SHA256SUMS.txt eklendi. CI ve ayrı elle çalıştırılan canlı test akışı hazırlandı.

Doğrulama sınırı: Yerel Windows x64 doğrulaması yapıldı; temiz bir Windows sanal makinesi, Linux/macOS çalışma zamanı ve GitHub üzerinde uzaktan yayın/güncelleme kurulumu bu oturumda denenmedi. Bu platform ve dağıtım kabul kontrolleri yayın öncesinde çalıştırılmalı. "Sonraki değerlendirmeler" tablosu bu teslimin kapsamından sonraki seçenekleri belirtir.

## Başlangıç durumu

- Kaynak sürümü 1.15.4; Avalonia arayüzü ve ayrı bir Core projesi mevcut.
- YoutubeExplode ve dönüştürücü bağımlılığı 6.6.2'ye yükseltildi. Kısa bir herkese açık videoda MP4 ve MP3 indirme ve dosya çözümleme kontrolleri geçti.
- Uygulamanın güncelleme ve sürüm bağlantıları MrHakan/YoutubeDownloader deposuna yönlendirildi. Otomatik güncelleme yeni ayarlarda varsayılan olarak kapalı.
- Windows x64 için FFmpeg içeren bağımsız paket üretildi. Debug derlemesi sıfır uyarı ve hatayla tamamlandı.
- Çoklu indirme, kalite seçimi, altyazı, medya etiketleri ve elle yeniden deneme zaten mevcut. Bunları geliştirmek gerekiyor.
- Çalıştırılan indirme denemeleri yerel doğrulamalar; çözümde kalıcı bir test projesi henüz yok.

İlk teslimler Windows x64 üzerinde doğrulanacak. Mevcut diğer platform hedefleri korunacak; her platformun yayınlanması kendi doğrulamasına bağlı olacak. Aşağıdaki sürüm numaraları önerilen teslim aşamalarıdır.

## Öncelik ve teslim sırası

| Aşama | Öncelik | Önerilen sürüm | Sonuç |
| --- | --- | --- | --- |
| 1 | P0 — kritik | 1.15.5 | Hata veya iptal mevcut dosyaları kaybettirmiyor; temel regresyon testleri mevcut |
| 2 | P1 — yüksek | 1.16.0 | Ağ hatalarında kontrollü yeniden deneme, güncel indirme bağlantıları ve anlaşılır hata mesajları |
| 3 | P1 — yüksek | 1.16.1 | Kalıcı kuyruk, düzenli uygulama kapanışı ve kalıcı kullanıcı tercihleri |
| 4 | P2 — ürün iyileştirmesi | 1.17.0 | Türkçe arayüz, indirme profilleri ve daha iyi ilerleme bilgisi |
| 5 | P1 — yayın öncesi gerekli | Her sürüm | Test edilmiş, doğrulanabilir ve yalnızca özel depodan dağıtılan paket |

Aşama 5'in derleme ve test altyapısı Aşama 1'de kurulacak; yayın kontrolleri her teslimde uygulanacak. Her aşama kendi kabul ölçütlerini karşılamadan tamamlanmış sayılmayacak.

## Aşama 1 — Dosya güvenliği ve regresyon testleri

### 1.1 Güvenli dosya yazımı

**Kodda görülen durum:** Tekli indirme onayında hedef dosyaya boş içerik yazılıyor. Kuyruk hata işleyicisi hedef dosyayı siliyor. Kullanıcı mevcut bir dosyanın üzerine yazmayı seçtiğinde, indirme tamamlanmadan eski içerik kaybolabiliyor.

- [x] İndirme ve dönüştürme için hedefle aynı diskte, işe özel geçici dosyalar kullan.
- [x] Başarılı sonuçtan sonra hedef dosyayı değiştir; değişiklik başarısız olursa eski dosyayı koru.
- [x] Temizlik işlemlerini yalnızca ilgili işin oluşturduğu geçici dosyalarla sınırla.
- [x] Tekli ve çoklu indirmelerde hedef adını ayırmak için dosyayı boşaltmak yerine kuyrukta yol rezervasyonu tut.
- [x] İki iş aynı hedefe yöneldiğinde benzersiz ad veya açık bir üzerine yazma seçimi uygula.

**Kabul ölçütleri:** Var olan dosyanın üzerine indirme seçilip ağ hatası, FFmpeg hatası veya iptal oluşturulduğunda eski dosyanın içeriği aynı kalmalı. Aynı adı isteyen eşzamanlı iki iş birbirinin çıktısını bozmamalı. Başarılı indirmeden sonra çıktı açılmalı ve işe ait geçici dosyalar temizlenmeli.

### 1.2 Kalıcı test temeli

- [x] Core davranışları ve kuyruk/dosya yönetimi için takip edilen test projeleri ekle.
- [x] Test edilecek ağ, dönüştürme ve dosya işlemlerine gereken küçük bağımlılık sınırlarını ekle; kapsamlı mimari değişikliğe gitme.
- [x] Kalite/format seçimi, dosya adı üretimi, çakışma, iptal ve dosya koruma senaryolarını ağsız test et.
- [x] Güncellemenin varsayılan olarak kapalı olduğunu ve kaynak deponun MrHakan/YoutubeDownloader olduğunu doğrula; eski ayarlarda güncelleme açık kalması senaryosunu da kapsa.
- [x] Gerçek MP4/MP3 indirmelerini ayrı, açıkça çalıştırılan entegrasyon testlerine taşı. Çerez veya özel video kullanma.

**Kabul ölçütleri:** Normal testler internet gerektirmeden geçmeli. Dosya kaybına neden olan eski davranış regresyon testini başarısız kılmalı. Canlı test raporu video kimliğini, kullanılan sürümü ve başarılı/başarısız aşamayı göstermeli.

## Aşama 2 — İndirme dayanıklılığı ve hata teşhisi

### 2.1 Güncel akış bilgisi ve yeniden deneme

**Kodda görülen durum:** Tekli indirmede format seçilirken alınan akış bilgisi kuyrukta tutuluyor; elle yeniden deneme de aynı bilgiyi kullanıyor. Uzun beklemelerde bu bağlantıların geçerliliğini yitirmesi olası bir hata kaynağı.

- [x] Kuyrukta format, kalite ve ses dili tercihini sakla; aktarım başlarken akış bilgilerini yeniden çözümle.
- [x] Kullanıcının seçtiği kalite yoksa tanımlı bir alternatif sun; kaliteyi sessizce düşürme.
- [x] Geçici ağ hatalarında sınırlı, artan bekleme süreli yeniden deneme uygula; başlangıç hedefi en fazla üç deneme.
- [x] Erişimi olmayan/silinmiş video, geçersiz hedef, eksik FFmpeg ve kullanıcı iptalinde otomatik yeniden deneme yapma.
- [x] Bekleme, çözümleme ve aktarım boyunca iptal isteğini geçir. Yetkilendirme hatasında aynı eski bağlantıyla sürekli denemek yerine bilgiyi yenile ve sonucu kullanıcıya bildir.

**Kabul ölçütleri:** Süresi geçmiş akış bilgisi ve geçici ağ kesintisi testlerinde iş yeni bilgilerle ilerlemeli. İptal yeni denemeleri durdurmalı. Kalıcı hata kuyruktaki diğer işleri engellememeli.

### 2.2 İsteğe bağlı içerik ve hata mesajları

- [x] Altyazı veya etiket işlemi başarısız olduğunda ana medya indirilebiliyorsa işlemi tamamla ve hangi ek içeriğin eksik olduğunu belirt.
- [x] İptali isteğe bağlı içerik hatası olarak yutma.
- [x] Hataları bağlantı, erişim, format, dosya sistemi ve dönüştürme olarak sınıflandır; kullanıcıya uygun sonraki adımı göster.
- [x] Kullanıcı mesajından ayrı yerel teknik kayıt oluştur. Çerezleri, yetkilendirme başlıklarını ve imzalı medya adreslerini kaydetme; kayıt boyutunu sınırla.
- [x] URL/playlist çözümlemeye iptal ve makul zaman aşımı ekle.

**Kabul ölçütleri:** Altyazı isteği başarısız olduğunda geçerli medya dosyası korunmalı. Kullanıcı hatanın türünü ve yeniden denemenin anlamlı olup olmadığını görebilmeli. Kayıtlarda test amaçlı hassas değerler bulunmamalı.

## Aşama 3 — Kuyruk, ayarlar ve oturum yaşam döngüsü

- [x] Kuyruk çalışmasını ViewModel içindeki `async void` akışından, görevleri takip edilen bir servise taşı. Arayüz servis durumunu gözlemlesin.
- [x] İş kimliği, video kimliği, hedef, tercih ve durum bilgisini sürümlü bir kuyruk dosyasında sakla. Geçici akış adreslerini saklama.
- [x] Yeniden açılışta yarım kalan işleri göster; kullanıcı seçimiyle baştan başlat. İlk sürümde kaldığı bayttan devam etmeyi vaat etme.
- [x] Kuyruk sıralama, yalnızca başarısızları yeniden deneme ve yinelenen iş kontrolü ekle; mevcut yeniden deneme düğmelerini servis üzerinden çalıştır.
- [x] Kapanışta aktif işleri iptal edip kaynak temizliğini bekle; kuyruk durumunu kaydet.
- [x] Ayarları kullanıcıya ait yazılabilir bir dizinde tut; mevcut Settings.dat dosyasını yedekli ve bir kez yapılan geçişle aktar. Taşınabilir kullanım için açık bir seçenek bırak.
- [x] Oturum çerezlerinin mevcut saklama yöntemini doğrula; gerekiyorsa işletim sisteminin güvenli deposuna geçir. Oturumu temizleme ve kaydetmeme seçeneklerini koru.

**Kabul ölçütleri:** Yeniden açılışta tercihler ve bekleyen işler geri gelmeli. Bozuk kuyruk/ayar dosyası uygulamanın açılmasını engellememeli. Eşzamanlılık limiti değişimi ve bekleyen işi iptal etme kilitlenmeye yol açmamalı. Yeniden başlatılan işler güncel akış bilgisi almalı.

## Aşama 4 — Kullanım deneyimi

- [x] Türkçe ve İngilizce arayüz metinlerini kaynak dosyalarına taşı; dil tercihini sakla.
- [x] “En iyi video”, “1080p”, “720p” ve “MP3” gibi düzenlenebilir indirme profilleri ekle. Tekli indirme ve toplu indirmede aynı tercih mantığını kullan.
- [x] İndirme, dönüştürme ve tamamlanma aşamalarını ayrı göster. Aktarım hızı ve kalan süreyi yalnızca ölçülebilen aktarım aşamasında göster.
- [x] Son kullanılan hedef klasörü hatırla; klasörü açma, klavyeyle işlem ve sürükleyip bağlantı bırakma akışlarını iyileştir.
- [x] Playlist seçiminde arama, toplam/seçili video sayısı ve kullanılamayan videoları göster.
- [x] Özel uygulama adı, Hakkında ekranı, sürüm notları ve destek bağlantılarını düzenle; kaynak projenin atıflarını koru.

**Kabul ölçütleri:** Kullanıcı bir bağlantıyı yapıştırıp kayıtlı profil ve klasörle indirmeyi başlatabilmeli. Uzun başlıklarda, yüksek ekran ölçeklemesinde ve klavye kullanımında temel işlemler erişilebilir olmalı. Türkçe seçildiğinde ana akışta İngilizce metin kalmamalı.

## Aşama 5 — Yayın ve bakım

- [x] CI akışına ağsız regresyon testlerini ve paket içeriği kontrollerini ekle. Canlı YouTube testlerini ayrı çalıştır; dış hizmet hatalarını raporda ayırt et.
- [x] Temiz dizinde, boşluk içeren proje yolunda ve önceden kurulmuş FFmpeg olmadan paket üretimini doğrula.
- [x] Paketlenen uygulama sürümü, FFmpeg ve gerekli çalışma zamanı dosyalarını kontrol et; SHA-256 özeti ve sürüm notları üret.
- [x] MrHakan deposunun paket adları ile güncelleme çözümleyicisinin beklediği adları eşleştir.
- [x] Güncelleme bulunan sürümü kullanıcıya göstererek başlatılsın; başarısız indirme mevcut çalışan uygulamayı korusun. Otomatik güncelleme kullanıcı açıkça etkinleştirene kadar kapalı kalsın.
- [x] Yayın akışındaki Discord bildirimi ve depo ayarlarını özel uygulamaya göre gözden geçir; yalnızca sahibinin yapılandırdığı hedef kullanılsın.
- [x] YouTube uyumluluk düzeltmelerini küçük değişikliklerle yayınla; bağımlılık değişikliklerini MP4, MP3, playlist ve iptal senaryolarıyla doğrula.

**Kabul ölçütleri:** Temiz Windows ortamında paketten MP4 ve MP3 indirilebilmeli. Uygulama orijinal downloader deposundan paket almamalı. Geliştirme derlemeleri otomatik güncellenmemeli. Yayınlanan paket, test edilen sürümle eşleşmeli.

## Sonraki değerlendirmeler

| Özellik | Ne zaman değerlendirilecek? |
| --- | --- |
| Duraklatma ve kaldığı yerden devam | Kalıcı kuyruk tamamlandıktan ve aktarım/dönüştürme sınırları belirlendikten sonra |
| Alternatif indirme motoru | Güncel YoutubeExplode ile tekrarlanabilir başarısızlıklar kalırsa; mevcut arayüzü koruyan küçük bir deneyle |
| Otomatik playlist takibi | Kullanıcı talebi ve kalıcı kuyruk hazır olduğunda |
| Linux/macOS teslimleri | İlgili platformlarda paket, FFmpeg ve arayüz testleri çalıştırılabildiğinde |

## Tamamlanan ilk iş paketi

1. Geçici çıktı ve hedef yol rezervasyonu tasarımını uygula.
2. Mevcut dosyanın hata/iptal durumunda korunduğunu doğrulayan testleri ekle.
3. Tekli ve çoklu indirmeyi yeni dosya akışına geçir.
4. MP4/MP3, üzerine yazma, aynı isimli işler ve iptal senaryolarını doğrula.
5. Kontroller geçtiğinde 1.15.5 için paket ve sürüm notlarını hazırla.

Bu iş paketi ve sonraki dört aşamanın geliştirmeleri 1.17.0 teslimine dahil edildi. Güncel kullanım ve test komutları Readme.md dosyasındadır.

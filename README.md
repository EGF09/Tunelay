<img src="src/Tunelay.App/Assets/Tunelay.png" width="96" height="96" alt="Tunelay logosu">

# Tunelay

Windows için Spotify masaüstü ve oyun içi kontrol kartı. Spotify masaüstü uygulamasını yönetir, müziği kendisi çalmaz.

Tunelay bağımsız bir projedir. Spotify tarafından yapılmamış, onaylanmamış veya desteklenmemiştir. Şarkı bilgisi ve kapaklar Spotify tarafından sağlanır. Spotify adı ve logosu Spotify AB'nin markalarıdır.

## İndirme

**[Tunelay 0.1.0-beta.1 kurulumunu indir](https://github.com/EGF09/Tunelay/releases/download/v0.1.0-beta.1/Tunelay-Setup-0.1.0-beta.1-x64.exe)** (66 MB) · [Sürüm notları ve SHA-256](https://github.com/EGF09/Tunelay/releases/tag/v0.1.0-beta.1) · [Tüm sürümler](https://github.com/EGF09/Tunelay/releases)

> Bu bir beta sürümüdür, hata içerebilir.

- **Gerekenler:** Windows 10 2004 (19041) veya üzeri 64 bit ve Spotify masaüstü uygulaması. .NET kurmanız ve yönetici izni vermeniz gerekmez.
- **Hesap şart değil:** kurunca **Hemen kullan** deyin. Hesap bağlamak isteğe bağlıdır, [sınırları var](#hesap-bağlama).
- **Windows uyarısı:** dosya dijital imzalı değil. SHA-256 özeti sürüm notlarındakiyle aynıysa **Ek bilgi → Yine de çalıştır** deyin. Özeti görmek için: `Get-FileHash .\Tunelay-Setup-0.1.0-beta.1-x64.exe`
- **Oyunlar:** hiçbir anti-cheat ile test edilmedi, önce [uyarıları](#oyunlar-ve-anti-cheat) okuyun.
- **Güncelleme ve kaldırma:** önce tepsiden Çıkış deyin. Yeni kurulumu eskisinin üzerine çalıştırın veya Windows Ayarları → Uygulamalar → Tunelay (Beta) → Kaldır.

Kurulumda gösterilen metinler: [kullanıcı sözleşmesi](installer/SOZLESME.txt), [gizlilik metni](installer/GIZLILIK.txt).

## Hesapsız ve hesapla

| | Hesapsız | Hesapla (Premium) |
|---|---|---|
| Şarkı, kapak, oynat, atla, karışık, tekrar | ✓ | ✓ |
| Ses | Spotify'ın sesi Windows mikserinde görünürken | ✓ |
| Şarkıya tıklayınca | Spotify'ı açar | şarkıyı Spotify'da açar |
| Ruh hali listeleri, beğenme, listeye ekleme | – | ✓ |

## Özellikler

- **Şarkı kartı:** odağı almaz, normalde şeffaftır ve tıklamaları alttaki pencereye geçirir.
- **Etkileşim tuşu:** düğmeleri açar. Esc, aynı tuş, farenin karttan çıkması veya 4 sn hareketsizlik kapatır.
- **Spotify bağlantısı:** kart açıkken kapak, şarkı adı, sanatçı ve logo Spotify'ı açar. Tam ekran oyunda yanlışlıkla çıkmamak için ikinci tık ister. Dar kartta yer kazanmak için boştayken yalnızca Spotify simgesi, etkileşimde tam logo görünür.
- **Beğenme ve listeye ekleme:** ⊕ Beğenilen Şarkılar'a ekler (✓ olur, tekrar basınca çıkarır). Liste simgesi şarkıyı seçili ruh halinin listesine ekler, liste size ait veya ortak değilse hata verir.
- **Ruh halleri:** her biri sizin seçtiğiniz bir çalma listesidir, karışık çalar. Adı, rengi ve sırası değiştirilebilir.
- **Spotify'ı arkada açma:** yalnızca Oynat'a veya bir ruh haline bastığınızda.
- **Windows ile başlatma:** varsayılan kapalı. Pencere açmadan, odak almadan başlar.
- **Taşıma modu:** kart ve ruh hali paneli herhangi bir monitöre sürüklenir.
- **Görünüm:** iki ayrı opaklık, köşe seçimi, kapatılabilir animasyonlar, oyunda ayrı animasyon seviyesi.

### Kısayollar

Hepsi Ayarlar'dan değiştirilebilir.

| İşlev | Varsayılan |
|---|---|
| Etkileşim tuşu | Ctrl+Alt+Space |
| Taşıma modu | Ctrl+Alt+D |
| Oynat / duraklat | Ctrl+Alt+P |
| Sonraki / önceki | Ctrl+Alt+→ / ← |
| Ses ±10 | Ctrl+Alt+↑ / ↓ |
| Karışık çalma | Ctrl+Alt+K |
| Tekrarlama | Ctrl+Alt+R |
| Beğen / vazgeç | Ctrl+Alt+L |
| Şarkıyı ruh hali listesine ekle | Ctrl+Alt+S |
| Sonraki ruh hali | Ctrl+Alt+M |
| Kenarlıksız tam ekran | Ctrl+Alt+B |

## Kaynaktan derleme

Yalnızca geliştirmek için gerekir, kullanmak için [kurulumu indirin](https://github.com/EGF09/Tunelay/releases/tag/v0.1.0-beta.1). Gereken: .NET 10 SDK.

Kurulum dosyası üretmek için (`artifacts\release\<sürüm>\`, [Inno Setup](https://jrsoftware.org/isinfo.php) 6.7 veya üzeri gerekir, `src` commit edilmiş olmalı):

```powershell
dotnet msbuild installer/Release.proj "-p:Iscc=C:\yol\ISCC.exe"
```

Tek dosyalık exe üretmek için (çıktı `publish\Tunelay.exe`, .NET kurulumu istemez, önce açık Tunelay'den çıkın):

```powershell
dotnet publish src/Tunelay.App -p:PublishProfile=Tunelay
```

Geliştirirken:

```powershell
dotnet run --project src/Tunelay.App
```

```powershell
dotnet test
```

## Hesap bağlama

İlk açılıştaki **Hesabımı bağla** veya Ayarlar → Spotify → **Hesap bağla…** üç adımlı sihirbazı açar:

1. [Spotify panelinde](https://developer.spotify.com/dashboard) Web API kullanan bir uygulama oluşturun.
2. Redirect URI olarak `http://127.0.0.1:5543/callback` ekleyin.
3. Client ID'yi sihirbaza yapıştırın. Client secret gerekmez.

Geliştirme modundaki bir Spotify uygulamasını en fazla 5 kişi kullanabilir. Her biri panelde User Management'a eklenir, uygulama sahibinin Premium'u olmalıdır.

### Bağlantıyı kesme

Ayarlar → Spotify → **Bağlantıyı kes…**. Spotify tarafındaki izni kaldırmak için ayrıca [spotify.com/account/apps](https://www.spotify.com/account/apps/) sayfasını kullanın.

## Oyunlar ve anti-cheat

Tunelay oyunlara DLL enjekte etmez, DirectX veya Vulkan'a kanca atmaz ve oyun belleğini okumaz. Kullandıkları: normal bir üstte duran pencere, `RegisterHotKey` ve süreç dışı `SetWinEventHook`.

**Bu, anti-cheat ile uyumlu olduğu anlamına gelmez. Hiçbir anti-cheat ile test edilmedi ve güvence verilmez.**

**Ctrl+Alt+B** diğerlerinden farklıdır: oyunun, yani başka bir sürecin penceresinin stilini ve boyutunu değiştirir. Rekabetçi veya anti-cheat kullanan oyunlarda kullanmayın.

Kart, kenarlıksız ve pencereli tam ekran oyunların üzerinde görünür. Özel (exclusive) tam ekranın üzerine masaüstü penceresi çizilemez.

## Gizlilik

Tunelay'in sunucusu, telemetrisi, reklamı ve çerezi yoktur. Geliştiriciye veri gönderilmez.

| Veri | Neden | Nerede | Ne zaman silinir |
|---|---|---|---|
| Erişim ve yenileme anahtarları | Oturum | `%AppData%\Tunelay\tokens.bin` (Windows DPAPI ile şifreli) | Bağlantıyı kesince |
| Seçilen listelerin kimliği ve adı | Ruh halleri | `%AppData%\Tunelay\settings.json` | Bağlantıyı kesince |
| Client ID, ruh halleri, kısayollar, görünüm | Ayarlar | `%AppData%\Tunelay\settings.json` | Kaldırırken silmeyi seçince veya klasörü siz silince |
| Kapak görseli (hesapsız mod) | Kartta gösterim | `%TEMP%\Tunelay-art` | Yeni kapakta veya bağlantıyı kesince |
| Başlangıç kaydı (açtıysanız) | Windows ile başlatma | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Anahtarı kapatınca veya kurulu sürümü kaldırınca |

- **Spotify'dan okunanlar:** çalma durumu ve cihazlar, çalma listeleriniz, çalan şarkının beğenilip beğenilmediği, listeye eklerken kullanıcı kimliğiniz (saklanmaz).
- **Spotify'da değiştirilenler:** yalnızca sizin komutunuzla oynatma, Beğenilen Şarkılar ve seçtiğiniz liste.
- **Bağlanılan adresler:** `accounts.spotify.com`, `api.spotify.com` ve Spotify'ın kapak görseli sunucuları. Görseller Windows'un geçici internet önbelleğinde kalabilir. Giriş kendi tarayıcınızda yapılır, oradaki çerezler Spotify'ın politikasına tabidir.
- **Windows'tan okunanlar:** Spotify'ın medya oturumu ve ses düzeyi, Spotify'ın açık olup olmadığı, öndeki pencerenin boyutu ve sınıfı (tam ekran algısı için).

Uygulamanın eski adı Spotiwidget idi. İlk açılışta `%AppData%\Spotiwidget` içindeki ayarlar ve oturum kopyalanır. Bağlantıyı kesmek oradaki Spotify verisini de siler.

## Beta'daki açık konular

Tunelay ücretsiz ve ticari olmayan bir beta olarak dağıtılıyor. Spotify tarafından incelenmedi veya onaylanmadı. [Developer Policy](https://developer.spotify.com/policy), [Developer Terms](https://developer.spotify.com/terms) ve [tasarım kuralları](https://developer.spotify.com/documentation/design) 22 Kasım 2026'da gözden geçirildi. Aşağıdakiler hukuki görüş değildir.

- [ ] **Client ID:** herkesin kendi kimliğini alması "uygulama başına tek kimlik" kuralıyla (Policy VII.2) çelişebilir. Tek kimlik ise geliştirme modunda 5 kişiyle sınırlı.
- [x] **Gizlilik metni:** kurulum sihirbazı kurmadan önce [GIZLILIK.txt](installer/GIZLILIK.txt) metnini gösterir (Terms V.12).
- [x] **Kullanıcı sözleşmesi (EULA):** kurulumda kabul edilmesi gereken [SOZLESME.txt](installer/SOZLESME.txt), Terms V.11'deki beş zorunlu maddeyi içerir. Hukukçu incelemesinden geçmedi.
- [ ] **Kod imzası:** kurulum imzasız, SmartScreen uyarı gösterir.
- [ ] **Hesapsız mod:** Premium olmayanlara kontrol sunmanın Policy IV.1 ile ilişkisi Spotify'a sorulmalı.
- [ ] **Temel deneyimi taklit:** Policy III.11 açısından Spotify'dan görüş alınmalı.
- [ ] **Kapak animasyonu:** şarkı değişiminde kapak kayarak geliyor, tasarım kuralları kapağın canlandırılmamasını istiyor.
- [ ] **Saydamlık:** kapak, şarkı bilgisi ve logo boştayken yarı saydam. Kurallar özgün görünüm ve okunabilirlik istiyor.
- [ ] **Boştayken yalnız simge:** kurallar buna "tam logoya yer yoksa" izin veriyor. 300 piksellik kartın böyle sayılacağı bizim yorumumuz, Spotify'a doğrulatılmalı.
- [ ] **Logo açılma animasyonu:** geçiş sırasında Spotify yazısı kısa süre yarım görünüyor. Animasyon açıkça yasak değil, ama "logo değiştirilmemeli" kuralı açısından gri alan.
- [ ] **Tunelay logosu:** marka benzerliği araştırması yapılmadı.
- [ ] **Oynatma kısıtları:** Spotify'ın izin vermediği komutların düğmeleri önceden kapatılmıyor, hata mesajı gösteriliyor.
- [ ] **Yeşil vurgu:** simge mor, ancak düğme vurguları Spotify yeşili.
- [ ] **Ticari kullanım:** oynatma kontrol eden uygulamalar satılamaz, reklam veya sponsor alamaz (Policy IV).

## Yol haritası

- Fare jestleri
- Zen modu ve Pomodoro
- Oyuna göre kart konumu
- Ayarların eşitlenmesi (Spotify verisi hariç)

Önce politika incelemesi gerekenler:

- Dinamik duvar kağıdı: kapak bulanıklaştırılamaz, kırpılamaz, üstüne bir şey konamaz.
- Ses kısma (ducking): başka sesle karıştırma yasağı (III.7).
- Oyun olayı tetikleyicileri: görsel medyayla eşzamanlama yasağı (III.6).
- Proaktif öneriler ve benzer şarkı radyosu: içerik analizi, profil çıkarma ve yapay zekâ yasakları (III.13, III.14).

Politika gereği yapılmayacaklar:

- **Sesli komut** (III.3)
- **Crossfade, şarkı geçişi karıştırma** (III.7)

## Proje yapısı

```
src/Tunelay.Core     ayarlar, PKCE, API istemcisi, oynatma, ruh halleri, başlangıç mantığı
src/Tunelay.Native   Win32, Windows medya oturumu, ses mikseri, DPAPI, kayıt defteri
src/Tunelay.App      WPF: kart, panel, karşılama, ayarlar, tepsi
tests/Tunelay.Core.Tests
installer            kurulum sihirbazı, sözleşme, gizlilik, lisanslar
```

# Spotiwidget

Masaüstü ve oyun içi Spotify atmosfer asistanı — **Faz 1 (çekirdek katman)**.

Masaüstüne sabitlenmiş mod düğmeleri, köşede sade bir şarkı kartı (önceki / oynat-duraklat / sonraki / beğen / mod listesine ekle) ve tam ekran oyunlarda şeffaf, tıklamaları geçiren bir görünüm.

## Özellikler (Faz 1)

- **Masaüstü ruh hali paneli** – Ruh hali düğmeleri masaüstünde, tüm pencerelerin arkasında durur. Ayarlardan kapatılabilir.
- **Kendi ruh halleriniz** – Ayarlar → Ruh halleri: ekleyin, yeniden adlandırın, renk ve çalma listesi seçin, silin. Sıra panelde ve Ctrl+Alt+M geçişinde kullanılır. Düğmeye basınca liste karışık çalar.
- **Şarkı kartı** – Kapak, şarkı, sanatçı, aktif ruh hali; 🔀 karışık çalma, ⏮ ⏯ ⏭, 🔁 tekrarlama (kapalı / liste / şarkı), ♡ (Beğenilen Şarkılar'a ekle/çıkar), ＋ (aktif ruh halinin listesine ekle), ses kaydırıcısı. Odağı asla çalmaz (`WS_EX_NOACTIVATE`).
- **Etkileşim modu** – Kart masaüstünde de oyunda da normalde şeffaf, sade ve tıklama geçirgendir. **Etkileşim tuşuna** basınca düğmeler açılır (oyunda ruh hali düğmeleri de karta gelir); Esc, tuşa tekrar basma, fare karttan çıkınca veya 4 sn hareketsizlikte kapanır. İki opaklık (boşta / etkileşimde) ayarlardan %0–100 seçilir.
- **Taşıma modu** – **Ctrl+Alt+D** veya tepsi menüsü → *Taşıma modu*: kart ve panel kesikli çerçeveyle belirir, istediğiniz yere (başka monitör dahil) sürükleyin; Esc veya tekrar Ctrl+Alt+D ile bitirin. Konumlar kaydedilir; Ayarlar → *Köşeye geri al* ile sıfırlanır.
- **Animasyonlar** – Şarkı değişimi, kartın açılması, ♡ "pop", bildirimler ve düğmeler için kısa (≤250 ms) geçişler. Yalnızca opaklık/kaydırma kullanılır, hiçbiri sürekli dönmez. Oyunda seviye Ayarlar → Görünüm → *Oyunda animasyonlar* ile seçilir: Kapalı · Hafif (30 fps, varsayılan) · Tam (60 fps). Windows animasyonları kapalıyken veya *Animasyonlar* anahtarı kapalıyken devre dışıdır.
- **Düğme içi animasyonlar** – Düğme yerinde durur, içindeki simge hareket eder: 🔀 oklar DNA sarmalı gibi kıvrılarak ilerler, 🔁 tam tur döner, ⏮/⏭ simge kayıp düğmeden çıkar ve karşı taraftan geri girer, ⏯ eski simge dönerek küçülüp yenisine dönüşür, ♡ beğenince büyür ve çevresine yeşil parçacıklar saçılır, ＋ listeye düşer gibi aşağı kayıp yukarıdan geri iner, ses değişince hoparlörden dalgalar yayılır (kısılınca içe), ruh hali düğmelerinde renk basılan noktadan yayılır. Kısayollarda ve Spotify uygulamasından yapılan değişikliklerde de oynar.
- **Şarkı değişimi vurgusu** – Yeni şarkı başlayınca kart ~3 sn etkileşim opaklığında belirginleşir, kapak/isim kayarak gelir, sonra soluklaşır; tıklamalar oyuna geçmeye devam eder. Oyunda da şarkı bitişi ~1 sn içinde fark edilir (yoklama şarkı sonuna göre zamanlanır).
- **Ayarlar penceresi** – Koyu tema, sekmeler (Spotify · Ruh halleri · Görünüm · Kısayollar), opaklık ve köşe için kartta canlı önizleme, kısayol çakışmalarında satır içi uyarı, ruh hallerini oklarla sıralama.
- **Tam ekran oyunlar** – Ön plandaki pencere monitörü kaplıyorsa kart her 2 sn'de en üste alınır ve Spotify yoklaması 3 sn → 15 sn'ye düşer.
- **Genel kısayollar** (ayarlardan değiştirilebilir; tek başına F1–F24, Pause, Scroll Lock veya Insert de atanabilir):

  | İşlev | Varsayılan |
  |---|---|
  | Etkileşim tuşu (kartı aç) | Ctrl+Alt+Space |
  | Taşıma modu | Ctrl+Alt+D |
  | Beğen / vazgeç | Ctrl+Alt+L |
  | Oynat / duraklat | Ctrl+Alt+P |
  | Karışık çalma aç / kapat | Ctrl+Alt+K |
  | Tekrarlama (kapalı → liste → şarkı) | Ctrl+Alt+R |
  | Sonraki / önceki | Ctrl+Alt+→ / ← |
  | Ses ±10 | Ctrl+Alt+↑ / ↓ |
  | Çalan şarkıyı aktif mod listesine kaydet | Ctrl+Alt+S |
  | Sonraki moda geç | Ctrl+Alt+M |
  | Oyunu kenarlıksız tam ekran yap / geri al | Ctrl+Alt+B |

- **Beğenme izni** – ♡ için `user-library-read` / `user-library-modify` izinleri gerekir. Daha önce bağlandıysanız bir kez Ayarlar → Bağlantıyı kes → Spotify'a bağlan yapın.
- **Güvenlik** – Token'lar Windows DPAPI ile şifrelenir (`%AppData%\Spotiwidget\tokens.bin`). Oyun süreçlerine DLL enjeksiyonu veya DirectX/Vulkan kancası yoktur; yalnızca `SetWinEventHook` (süreç dışı) ve `RegisterHotKey` kullanılır.

> **Not:** Kart, *kenarlıksız / pencereli tam ekran* oyunların üzerinde görünür. *Özel (exclusive) tam ekran* (ör. Minecraft'ta F11) üzerine hiçbir masaüstü penceresi çizilemez. Çözüm: oyunu pencere modunda açın, oyun penceresine tıklayın ve **Ctrl+Alt+B**'ye basın; pencere çerçevesiz olarak tüm ekranı kaplar ve kart üstünde görünür. Tekrar basınca eski hâline döner; Spotiwidget kapanırken de geri alınır. Oyuna hiçbir şey enjekte edilmez, yalnızca pencere stili ve boyutu değişir.

## Kurulum

### 1. Spotify geliştirici uygulaması

1. <https://developer.spotify.com/dashboard> → **Create app**.
2. **Redirect URI** olarak tam olarak şunu ekleyin: `http://127.0.0.1:5543/callback`
3. **APIs used** kısmında *Web API*'yi seçin, kaydedin.
4. **Client ID**'yi kopyalayın (Client Secret gerekmez — PKCE kullanılır).

Oynatma kontrolü (oynat, atla, ses, liste başlatma) **Spotify Premium** gerektirir.

### 2. Derleme ve çalıştırma

Gereken: .NET 10 SDK, Windows 10/11.

```powershell
dotnet build
```

```powershell
dotnet run --project src/Spotiwidget.App
```

İlk açılışta ayarlar penceresi açılır: Client ID'yi yapıştırın → **Spotify'a bağlan** → tarayıcıda onaylayın → her moda bir çalma listesi seçin → **Kaydet**. Uygulama sistem tepsisinde (♪) çalışır; sağ tık menüsünden ayarlar, gizle/göster ve çıkış.

### Testler

```powershell
dotnet test
```

## Proje yapısı

```
src/Spotiwidget.Core     Ayarlar (JSON), PKCE yetkilendirme, token yenileme, rate-limit korumalı API istemcisi, oynatma yoklaması, mod servisi
src/Spotiwidget.Native   Win32: pencere stilleri, genel kısayollar, ön plan / tam ekran izleyici, DPAPI
src/Spotiwidget.App      WPF: şarkı kartı, masaüstü mod paneli, ayarlar penceresi, tepsi simgesi
tests/Spotiwidget.Core.Tests
```

Ayarlar: `%AppData%\Spotiwidget\settings.json`

## Sonraki fazlar

Vosk sesli komut + uyandırma kelimesi, fare jestleri, dinamik duvar kağıdı, audio ducking, Zen + Pomodoro zinciri, oyuna göre HUD konumlandırma, oyun içi olay tetikleyicileri, bulut senkronizasyonu, proaktif öneriler, benzer şarkı radyosu.

# Tunelay {VERSION}

Tunelay'in ilk beta sürümü: Windows için Spotify masaüstü ve oyun içi kontrol kartı.

## Neler var

- Şeffaf, tıklamaları geçiren şarkı kartı. Ctrl+Alt+Space ile düğmeleri açılır.
- Hesapsız kullanım: şarkı, kapak, oynat, atla, karışık, tekrar, ses.
- Hesapla (Spotify Premium): ruh hali listeleri, beğenme, listeye ekleme.
- Spotify kapalıysa Oynat'a basınca arkada açma.
- İsteğe bağlı Windows ile başlatma.
- Taşınabilir kart ve panel, ayarlanabilir kısayollar, opaklık ve animasyonlar.

## Kurulum

1. `Tunelay-Setup-{VERSION}-x64.exe` dosyasını indirip çalıştırın.
2. Dosya dijital imzalı olmadığı için Windows "bilinmeyen yayımcı" uyarısı gösterebilir. Yalnızca aşağıdaki SHA-256 tutuyorsa **Ek bilgi → Yine de çalıştır** deyin.
3. Sözleşmeyi ve gizlilik metnini okuyup kurun. Yönetici izni gerekmez.

Gerekenler: Windows 10 2004 (19041) veya üzeri, 64 bit ve Spotify masaüstü uygulaması. .NET kurmanız gerekmez.

## Bilinen sınırlar

- Hesap bağlamak için Spotify geliştirici panelinden kendi Client ID'niz veya bir uygulama sahibinin sizi test kullanıcısı olarak eklemesi gerekir (uygulama başına en fazla 5 kişi). Hesapsız mod bunları istemez.
- Özel (exclusive) tam ekran oyunların üzerinde görünmez. Kenarlıksız veya pencereli tam ekran kullanın.
- **Hiçbir anti-cheat ile test edilmedi.** Ctrl+Alt+B'yi rekabetçi veya anti-cheat kullanan oyunlarda kullanmayın.
- Otomatik güncelleme yok. Yeni sürümün kurulumunu eskisinin üzerine çalıştırın, ayarlarınız kalır.
- Yalnızca Türkçe.

## Kaldırma

Önce tepsiden Çıkış, sonra Windows Ayarları → Uygulamalar → Tunelay (Beta) → Kaldır. Ayarların ve Spotify oturumunun silinip silinmeyeceği sorulur.

## SHA-256

```
{SHA256}
```

PowerShell ile denetlemek için:

```powershell
Get-FileHash .\Tunelay-Setup-{VERSION}-x64.exe
```

Tunelay bağımsız bir projedir, Spotify ile bağlantılı değildir. Sorun bildirimi: https://github.com/EGF09

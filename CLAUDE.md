# NemraPOS

Mevcut NemraOne POS yazılımı (`pos/`, WPF/C#, offline-first) ile **MikroERP V16/V17** arasındaki sunucu: `kernel/`.

## Yapı
- `web/` — pos.nemra.net: tanıtım sitesi, **yardım merkezi** ve **lisans sunucusu** (Node.js/Express 5/EJS/`node:sqlite`). Müşteri/lisans yönetimi `/admin`'de; kernel `/api/v1/license/*` ile lisans alır. Ayrıntı: `web/README.md`.
- `pos/` — POS istemcisi (NemraONE projesinden kopyalandı). **Kernel işi için DEĞİŞTİRİLMEZ**; kernel POS'un mevcut REST sözleşmesine uyar (bkz. `pos/src/NemraPOS.Infrastructure/Api/`).
- `storehelper/` — StoreHelper (WinForms, .NET Framework 4.8, DevExpress 26.1): raf etiketi basımı (DevExpress Report Designer, tek etiket/A4) ve terazi CSV/TCP aktarımı. Kernel'e `/api/helper/*` ile panel kullanıcı adı/şifresiyle bağlanır; yerel SQLite. Test: `cd storehelper && dotnet test src/StoreHelper.Tests`. `distall.ps1` bunu da aynı sürümle derleyip `dist/downloads/NemraPOS-StoreHelper.zip` üretir (sürüm `versions.json`'da).
- `kernel/` — Node.js (ESM, düz JavaScript, derleme yok) + Express 5 + EJS + `mssql` + yerleşik `node:sqlite`. Ayrıntı ve mimari kararlar: `kernel/README.md`.
- Eski referans: `C:\arge\NemraONE\kernel` (Mongo, çok kiracılı). Buradaki kernel onun kopyası DEĞİL; yalnızca Mikro `CARI_HESAP_HAREKETLERI`/`STOK_HAREKETLERI` yazım bilgisi (`mikroColumns.js`) oradan alındı.

## Kurallar
- Kod/DB/endpoint isimleri **İngilizce**, kullanıcıya görünen metinler **Türkçe**.
- Mikro sorgularında dinamik değer **daima parametre** (`request.input`); string birleştirme yok.
- POS'tan gelen satış **reddedilmez** — doğrulanamayanlar `review`/`failed` olarak saklanır (para alınmış olabilir).
- Panelde native `alert/confirm` yok: `data-confirm` + `<dialog>`, bildirim için toast. Boolean alanlar `switch`, checkbox değil. Tıklanabilirlerde `cursor: pointer`.
- Yeni panel sayfası: `src/routes/admin/<ad>.js` (Router döndüren fonksiyon, `admin/index.js`'e ekle) + `src/views/pages/<ad>.ejs`; rol kısıtı `requireRole()`, menü girişi `lib.js` içindeki `NAV`.
- Kullanıcıya görünen davranış değişince ilgili yardım konusunu güncelle: `web/src/views/help/<slug>.ejs` (kayıt: `web/src/help/topics.js`; panel→konu eşlemesi `kernel/src/help/links.js` ile aynı tutulur). Yardım pos.nemra.net/help altında herkese açıktır; içine sır/ayar değeri yazma.
- SQLite şeması yalnızca `src/db/migrations.js`'e **yeni eleman eklenerek** değişir; mevcut migrasyon düzenlenmez.

## Test
- `cd web && npm test` (lisans API, yönetim paneli, yardım).
- `cd kernel && npm test` (birim) ve `node scripts/smoke.mjs` (uçtan uca; varsayılan Mikro'ya yazmaz).
- Yerel test Mikro veritabanı: SQL Server `.` / `MikroDesktop_TEK2026` (V17). Gerçek yazım testi yaparsanız işaretli (`NEMRAPOS`) kayıtları geri silin.
- Zamanlayıcı Mikro yapılandırılmış bir kernel'de bekleyen satışları **kendiliğinden** Mikro'ya yazar — test için önce "aktarımı duraklat".

## Web (`web/`)
- Veritabanı **MongoDB + Mongoose**, veritabanı adı `nemrapos` (küçük harf). Koleksiyonlar İngilizce çoğul, alanlar camelCase (`src/db/mongo.js`). `web` testleri çalışan bir MongoDB ister (geçici `nemrapos_test_*` DB'sini kullanıp siler). Kernel yerel SQLite kullanmaya devam eder.

## Dağıtım ve sürüm
- `.\distall.ps1` tüm modülleri (kernel, web, `kernel/setup` → NemraSetup.exe, pos) derleyip kök `dist/` altına toplar. Sürüm **tek kaynaktan** `kernel/package.json`'dan okunur; çalışınca `j`/`m`/`p` (major/minor/patch) ya da boş (değiştirme) sorar ve kernel, web, NemraSetup, pos'ta aynı sürümü yazar. Otomasyon: `-Bump p`, testleri atlamak için `-SkipTests`.
- `distall.ps1` ayrıca `dist/downloads/` altına sürümsüz adlarla `NemraPOS-Kernel.zip`, `NemraPOS-POS.zip`, `NemraPOS-StoreHelper.zip` ve sürüm bilgisini taşıyan `versions.json` üretir (her yayında üzerine yazılır); web sitesinin `/downloads` sayfası bunları `DOWNLOADS_DIR`'den sunar (`-DownloadsDir` ile oraya kopyalanır). Dosya adı biçimi `web/src/services/downloads.js` ile `distall.ps1` arasında sabittir.
- Sürümü elle düzenlemeyin; `web/src/version.js` ve `kernel/setup/NemraSetup.csproj` `distall.ps1` tarafından eşitlenir. Yalnızca pos için: `.\buildpos.ps1`.

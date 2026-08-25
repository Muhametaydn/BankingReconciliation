# Banking Reconciliation Project Phases

Bu dosya projede simdiye kadar ne yaptigimizi, hangi fazda oldugumuzu ve siradaki mantikli adimlari takip etmek icin tutuluyor.

## Current Snapshot

Proje su anda calisan bir .NET 8 ASP.NET Core reconciliation uygulamasi.

- UI branch ve bank dosyalarini yukluyor.
- API `BranchCode + FundCode + TransactionNumber` anahtari ile kayitlari eslestiriyor.
- Sonuclar `Matched`, `OnlyInBranch`, `OnlyInBank`, `QuantityMismatch`, `AmountMismatch`, `QuantityAndAmountMismatch` olarak donuyor.
- CSV ve TXT destekleniyor.
- PostgreSQL persistence var; connection string doluysa PostgreSQL, test ortaminda/in-memory fallback kullaniliyor.
- Excel fark raporu var.
- History ve audit kayitlari var.
- Tamamlanan mutabakatlar icin JWT yetkili onay/red karari ve karar audit alanlari var.
- Kaynak, sema ve karsilastirma ayari degisiklikleri JWT yonetim yetkisiyle korunuyor ve before/after audit kaydi uretiyor.
- Kiyaslama davranislari config ile yonetiliyor.
- Son bilinen test durumu (2026-08-20): `272/272` test basarili.

## Phase 1 - Core Reconciliation

Status: done.

Bu fazda temel mutabakat motoru kuruldu.

Implemented:

- Branch ve bank transaction modelleri.
- `BranchCode + FundCode + TransactionNumber` matching key.
- Matched kayitlari bulma.
- Sadece sube tarafinda olanlari bulma.
- Sadece banka tarafinda olanlari bulma.
- Quantity farki.
- Amount farki.
- Hem quantity hem amount farki.
- Duplicate transaction key kontrolu.
- Servis seviyesinde regression testleri.

## Phase 2 - File Upload And Validation

Status: done.

Bu fazda dosya alma ve dosya validasyonu eklendi.

Implemented:

- Multipart upload endpoint: `POST /api/reconciliations/compare`.
- CSV parser.
- TXT dosya kabul etme.
- Comma, pipe `|`, tab delimiter destegi.
- Header validasyonu.
- Kolon sayisi validasyonu.
- Required field validasyonu.
- `TransactionDate` icin `yyyy-MM-dd` validasyonu.
- `Quantity` ve `Amount` decimal validasyonu.
- File extension validasyonu.
- File size validasyonu.
- Hata response'larinda `rowNumber`.
- Biliniyorsa hata response'larinda `columnName`.

Later extensions:

- Gercek kaynak ornekleri geldikce yeni dosya formati ve schema kural tipleri eklenebilir.

## Phase 3 - Frontend Review UI

Status: done for first usable version.

Bu fazda uygulamayi kullanilabilir hale getiren basit frontend eklendi.

Implemented:

- Dosya secme UI.
- Karsilastirma butonu.
- Ozet metrikler.
- Sonuc tablosu.
- Status filtreleri.
- History listesi.
- Eski batch secme.
- Excel indir butonu.
- Turkce durum aciklamalari.
- Sonuc satirlarinda status'a gore renklendirme.
- Failed batch sebeplerini history listesinde gosterme.

Still later:

- Daha yogun operasyon ekranina uygun ek tasarim iyilestirmeleri.
- Daha yogun operasyon ekranina uygun tasarim iyilestirmeleri.

## Phase 4 - Excel Reporting

Status: done for first version.

Bu fazda farklarin raporlanmasi eklendi.

Implemented:

- Export endpoint: `GET /api/reconciliations/{id}/export`.
- Excel `.xlsx` cikti.
- Sadece fark satirlari rapora yaziliyor.
- Matched satirlar rapora yazilmiyor.
- Fark aciklamalari:
  - `Adet sube tarafinda fazla gorunuyor.`
  - `Adet banka tarafinda fazla gorunuyor.`
  - `Sadece sube tarafinda var.`
  - `Sadece banka tarafinda var.`

Still later:

- PDF ozet rapor.
- Excel icin daha zengin stil/filtre.
- Failed batch icin ayri hata raporu.

## Phase 5 - Persistence And PostgreSQL

Status: done for first production-like version.

Bu fazda in-memory history'den PostgreSQL destekli kalici yapiya gecildi.

Implemented:

- EF Core PostgreSQL package.
- `ReconciliationDbContext`.
- `ReconciliationBatches` tablosu.
- `ReconciliationDifferences` tablosu.
- `ReconciliationSources` tablosu.
- Branch ve bank/source seed kayitlari.
- PostgreSQL repository.
- In-memory fallback.
- EF Core migrations.
- Startup'ta migration uygulama.
- `appsettings.json` icinde PostgreSQL connection string.
- Test icin ayri optional PostgreSQL connection string:
  - `BANKING_RECONCILIATION_POSTGRES_TEST_CONNECTION`

Database bloat kararimiz:

- Tum raw input satirlari veritabaninda tutulmuyor.
- Matched satirlar veritabanina yazilmiyor.
- Batch summary tutuluyor.
- Sadece non-matched difference satirlari tutuluyor.
- Failed batch icin raw row degil, hata kodu ve mesaj tutuluyor.

Indexes:

- Batch `CreatedAt`.
- Batch `Status`.
- Batch `ErrorCode`.
- Difference `BatchId`.
- Difference `Status`.
- Unique source key: `Type + Code`.
- Unique difference key per batch: `BatchId + BranchCode + FundCode + TransactionNumber`.

## Phase 6 - History, Audit And Status

Status: done for first audit version.

Bu fazda mutabakat calismalarinin izlenebilirligi eklendi.

Implemented:

- History endpoint: `GET /api/reconciliations`.
- Batch detail endpoint: `GET /api/reconciliations/{id}`.
- Batch status:
  - `Completed`
  - `Failed`
- Processing duration:
  - `ProcessingDurationMilliseconds`
- Basarili batch metadata.
- Parse hatasi alan batch'leri `Failed` olarak kaydetme.
- Duplicate key hatasi alan batch'leri `Failed` olarak kaydetme.
- Failed batch icin:
  - `ErrorCode`
  - `ErrorMessage`
  - file names
  - processing duration
  - zero summary counts

Still later:

- Ayar ve kaynak degisikliklerini kapsayan genel kullanici/action audit trail.

## Phase 7 - Configurable Comparison Rules

Status: done for current scope.

Bu faz mentor notlarindaki "koda gomme, config ile yonet" kismidir.

Implemented config:

- `NormalizeCodeCase`
- `TrimTextValues`
- `TrimBranchCode`
- `TrimFundCode`
- `TrimTransactionNumber`
- `QuantityDecimalPlaces`
- `BranchQuantityDecimalPlaces`
- `BankQuantityDecimalPlaces`
- `AmountDecimalPlaces`
- `BranchAmountDecimalPlaces`
- `BankAmountDecimalPlaces`
- `BranchCodeMappings`
- `FundCodeMappings`
- `TransactionNumberMappings`

Implemented behavior:

- Buyuk/kucuk harf normalize edilebiliyor.
- Branch code mapping config'den yapiliyor.
- Fund code mapping config'den yapiliyor.
- Transaction number mapping config'den yapiliyor.
- Text alanlar trimlenebiliyor veya bosluklar anlamli kabul edilebiliyor.
- `BranchCode`, `FundCode`, `TransactionNumber` icin trim davranisi ayri override edilebiliyor.
- Quantity ve amount icin ortak decimal precision verilebiliyor.
- Branch ve bank icin ayri decimal precision verilebiliyor.

Examples:

- `BEYLIKDUZU SUBE` -> `BEYLIKDUZU`
- `A FONU` -> `A`
- `FON_A` -> `A`
- `TX-001` -> `TX001`
- Sube quantity 2 decimal, banka quantity 3 decimal tutuluyorsa config ile ayarlanabiliyor.
- Sadece `TransactionNumber` trimlenip `BranchCode` bosluklari korunabiliyor.

Later extensions:

- Yeni gercek kaynak ihtiyaclari geldikce kaynak bazli donusum kurallari genisletilebilir.

## Phase 8 - CI And Regression Safety

Status: done for current scope.

Implemented:

- Regression tests before behavior changes.
- Parser tests.
- Service tests.
- Endpoint tests.
- Frontend/static asset tests.
- PostgreSQL repository tests.
- Optional PostgreSQL integration tests.
- GitHub Actions workflow.
- CI icinde PostgreSQL service container.

Current known test count:

- `272/272` normal ve MinIO/PostgreSQL/AWS profilleri disinda kosullu integration testleri pass.
- PostgreSQL integration tests env var ile calisacak sekilde hazir.

## Phase 9 - Field And Schema Validation

Status: done for current generic schema scope.

Bu faz mentor notlarindaki "kolon tipi nedir?", "3. sutundaki veriler beklenen tipe uyuyor mu?" sorularina giden altyapidir.

Implemented:

- Parser icinde merkezi fixed column schema tanimi.
- Schema kolonlari:
  - `BranchCode`: text, required
  - `FundCode`: text, required
  - `TransactionNumber`: text, required
  - `TransactionDate`: date, required, `yyyy-MM-dd`
  - `Quantity`: decimal, required
  - `Amount`: decimal, required
- Satir validasyonu schema uzerinden calisiyor.
- Schema endpoint: `GET /api/reconciliation-file-schema`.
- Schema validation endpoint: `POST /api/reconciliation-file-schema/validate`.
- Frontend file schema preview panel.
- Schema preview icin rule aciklamalari.
- File schema config altyapisi.
- Kolon sirasini ve header adlarini config'den okuma.
- Configurable `Integer` schema rule.
- Frontend `Validate et` action for branch and bank files.
- Amount decimal validasyonu icin ek regression test.
- Schema endpoint icin endpoint testi.
- Schema validation endpoint icin valid/invalid endpoint testleri.
- Configured schema order/header name parser testi.
- Integer schema rule icin parser ve endpoint testleri.
- Schema ayarlari icin merkezi startup validator.
- Buyuk/kucuk harf ve cevre bosluklarini dikkate alan benzersiz header adi kontrolu.
- Schema ayar validator regression testleri.
- On validasyonda tum satir ve kolon hatalarini tek istekte toplama.
- Branch ve bank validasyon hatalarini arayuzde ayri satirlarda gosterme.
- Source-specific decimal config validation genisletildi.
- Kolon bazli regex `Pattern` ve `PatternDescription` config destegi.
- TransactionNumber icin default pattern ornegi.
- Pattern kurallari parser, schema endpoint, frontend preview ve startup validator tarafinda destekleniyor.
- Runtime schema update endpoint eklendi: `PUT /api/reconciliation-file-schema`.
- Frontend schema editor eklendi; header, type, required, date format, pattern ve pattern aciklamasi ekrandan guncellenebiliyor.
- Kolon bazli `MinLength` ve `MaxLength` generic rule destegi eklendi.
- Kolon bazli `AllowedValues` generic rule destegi eklendi.
- Kolon bazli `MinValue` ve `MaxValue` numeric range rule destegi eklendi.
- Kolon bazli `MaxDecimalPlaces` decimal scale rule destegi eklendi.
- `FieldMappings` generic value mapping destegi eklendi; eski Branch/Fund/TransactionNumber mapping ayarlari korunuyor ve oncelikli calisiyor.
- `MatchingFields` config destegi eklendi; mutabakat anahtari artik config ile `BranchCode + TransactionNumber` gibi varyantlara cekilebiliyor.
- `ComparisonFields` config destegi eklendi; Quantity ve Amount fark kontrolleri config ile acilip kapatilabiliyor.
- `ResultFields` config destegi eklendi; API response `fieldValues` ile dinamik sonuc kolonlarini donduruyor ve frontend tablo kolonlarini buna gore ciziyor.
- Schema artik required 6 kolon disinda extra kolon kabul ediyor.
- Extra kolonlar validate ediliyor, `TransactionRecord.ExtraFields` icinde tasiniyor ve `ResultFields` ile sonuc kolonlarina alinabiliyor.
- Extra numeric kolonlar `ComparisonFields` ile karsilastirilabiliyor.
- Extra numeric farklar `FieldMismatch` status'u ve `fieldDifferences` response alani ile donuyor.
- Excel export extra numeric farklar icin dinamik `{Field}Difference` kolonlari uretiyor.
- PostgreSQL difference kayitlari extra field ve field difference verilerini JSON kolonlarda sakliyor.
- Dosya semasi ayarlari PostgreSQL'e kalici kaydediliyor ve uygulama acilisinda geri yukleniyor.
- Karsilastirma ayarlari icin `GET/PUT /api/reconciliation-comparison-settings` endpointleri eklendi.
- Matching, comparison, result, trim, precision ve mapping ayarlari frontend'den yonetilebiliyor.
- Karsilastirma ayarlari aktif schema ile uyumluluk kontrolunden geciyor.
- Karsilastirma ayarlari PostgreSQL'e kalici kaydediliyor ve uygulama acilisinda geri yukleniyor.
- Masaustu ve mobil ayar ekrani tarayici ile dogrulandi.
- Reconciliation source kayitlari icin ad, aciklama ve aktiflik guncelleme endpointi eklendi.
- Veri kaynaklari frontend'den duzenlenebiliyor; masaustu ve mobil gorunum dogrulandi.
- Veritabani kaynaklari named connection string ve salt-okunur query ile yapilandirilabiliyor.
- Baglanti degeri, baglanti adi ve sorgu API response'larinda gizli tutuluyor.
- Frontend kaynak bazinda yalnizca `Veritabani hazir/eksik` durumunu gosteriyor.
- PostgreSQL kaynak okuyucu salt-okunur repeatable-read transaction ile kayit cekiyor.
- Veritabani kolonlari aktif schema alanlarina cevriliyor; mapping, normalization ve extra field destegi korunuyor.
- Kaynak okuma hatalari baglanti bilgisini ifsa etmeden source-specific hata uretiyor.
- `POST /api/reconciliations/compare-database-sources` endpointi BRANCH ve BANK kaynaklarini paralel okuyup mevcut motorla karsilastiriyor.
- Veritabani mutabakati completed/failed history kaydi ve mevcut Excel export akisini kullaniyor.
- Frontend veritabani kaynaklari hazir ve aktif oldugunda karsilastirma dugmesini etkinlestiriyor.
- Performance regression testleri 10 bin CSV satiri ve kaynak basina 25 bin reconciliation kaydi icin eklendi.
- Son local debug olcumu: parser yaklasik 97 ms / 30 MB, comparison yaklasik 151 ms / 53 MB allocation.
- Buyuk veri regression seviyeleri parser icin 50 bin, comparison icin kaynak basina 75 bin kayda genisletildi.
- Son buyuk veri olcumu: 50 bin satir parser yaklasik 817 ms / 151 MB; 75 bin + 75 bin comparison yaklasik 649 ms / 160 MB allocation.
- Dosya ve veritabani kaynaklari icin yapilandirilabilir 100 bin kayit limiti eklendi.
- Limit asimlari dosya on dogrulamasinda ve veritabani kaynak hatasinda acik sebep ile durduruluyor.
- History API icin varsayilan 50, en fazla 200 kayitlik sunucu tarafli sayfalama eklendi.
- History API ve frontend dosya/hata aramasi ile tarih araligi filtresini destekliyor.
- History filtreleri ve sayfalama masaustu ile mobil gorunumde dogrulandi.
- History sayfalama toplam kayit sayisini response header ile alip kesin onceki/sonraki durumu gosteriyor.
- Completed/Failed history durum filtresi eklendi.
- History aramasi 200 karakterle sinirlandi ve PostgreSQL joker karakterleri literal arama icin escape ediliyor.
- Statik CSS/JavaScript dosyalari icin cache-busting surum parametresi eklendi.
- Fixed-width TXT kolonlari icin bir tabanli baslangic ve uzunluk schema ayarlari eklendi.
- Tam, pozitif ve cakismayan fixed-width tanimlari startup/runtime validation ile zorunlu tutuluyor.
- Fixed-width header ve veri satirlari mevcut validation, comparison, history ve export akisina baglandi.
- Fixed-width ayarlari frontend schema editor ve kalici schema deposunda destekleniyor.
- Veritabani mutabakati icin `202 Accepted` donduren background-job endpointi eklendi.
- Sinirli 100 islik kuyruk ve tek okuyuculu hosted worker eklendi.
- Batch durumlari `Queued`, `Processing`, `Completed` ve `Failed` olarak izleniyor.
- Background isler UI'dan baslatilip otomatik izlenebiliyor; senkron endpoint korunuyor.
- Persisted queued isler acilista geri aliniyor, yarida kesilen isler acik hata koduyla kapatiliyor.
- Dosya mutabakati icin `POST /api/reconciliations/compare/jobs` background endpointi eklendi.
- Yuklenen dosyalar client dosya adindan bagimsiz batch klasorlerine kontrollu stream copy ile yaziliyor.
- Dosya boyutu kopyalanan gercek byte sayisinda tekrar dogrulaniyor; basari ve hatada gecici dosyalar temizleniyor.
- File ve database batch'leri `InputType` ile ayriliyor; restart recovery yanlis worker'a is vermiyor.
- File background akisi UI'dan baslatilip ayni history/detail endpointlerinden izleniyor.
- Elle eklenmis kalici schema/comparison migration'lari EF discovery metadata'siyle duzeltildi ve regression testi eklendi.
- Windows Event Log izni olmayan ortamlarda startup'i dusuren logger kaldirildi; console/debug logging korunuyor.
- Yeni eklenen zorunlu kolonlar sonuc alanlarina, yeni sayisal kolonlar ise sonuc ve karsilastirma alanlarina otomatik ekleniyor.
- Silinen veya sayisal tipten cikarilan kolonlara ait gecersiz sonuc, karsilastirma ve generic mapping ayarlari otomatik temizleniyor.

Still later:

- Daha fazla generic rule tipi: bu faz icin temel text/numeric rule seti tamamlandi; yeni ihtiyaca gore eklenebilir.
- Schema rule setlerini daha generic hale getirme.

## Phase 10 - Approval And Authorization

Status: done for first production-ready contract.

Implemented:

- Completed batch'ler `Pending` onay durumuna geciyor.
- `Approved`, `Rejected` ve `NotApplicable` durumlari eklendi.
- Karari veren kullanici, karar zamani ve aciklama batch uzerinde audit alani olarak tutuluyor.
- Red karari icin aciklama zorunlu ve 1000 karakterle sinirli.
- Ayni batch icin ikinci veya eszamanli karar atomik olarak `409 Conflict` ile engelleniyor.
- `POST /api/reconciliations/{id}/approval` endpointi eklendi.
- Standart JWT Bearer authentication eklendi.
- Yetki, config'deki approver role veya permission claim ile veriliyor.
- Yetkisiz ve yetkisi olmayan istekler `401/403` ile engelleniyor.
- Frontend onay paneli, karar durumu ve audit bilgisini gosteriyor.
- Erisim anahtari frontend tarafinda kalici depoya yazilmiyor.
- PostgreSQL migration'i mevcut completed batch'leri `Pending` durumuna tasiyor.
- Endpoint, repository, mapper, migration ve frontend regression testleri eklendi.
- Masaustu ve mobil onay paneli tarayici ile dogrulandi.

## Phase 11 - Management Audit Trail

Status: done for first production-ready version.

Implemented:

- `ReconciliationAuditEvents` PostgreSQL tablosu ve in-memory fallback eklendi.
- Actor, UTC zaman, action, resource type/id ve onceki/sonraki durum JSON olarak tutuluyor.
- Veri kaynagi, dosya semasi ve karsilastirma ayari guncellemeleri audit kaydi uretiyor.
- Onay ve red kararlari ortak audit listesine de yaziliyor.
- Connection string, source query ve erisim token'lari audit payload'ina alinmiyor.
- Yonetim islemleri `ReconciliationAdministrator` rolu veya `reconciliation.manage` permission ile korunuyor.
- `GET /api/reconciliation-audit-events` actor, tarih, action, resource type ve sayfalama filtrelerini destekliyor.
- Toplam kayit sayisi `X-Total-Count` response header'inda donuyor.
- Frontend tek bir yonetim erisim anahtariyla guncelleme ve audit listeleme yapiyor; token kalici depoya yazilmiyor.
- Audit ekraninda Turkce islem adlari, kullanici/kaynak bilgisi ve acilabilir once/sonra gorunumu var.
- Migration gercek yerel PostgreSQL veritabanina uygulandi.
- Masaustu ve mobil UI yatay tasma olmadan tarayici ile dogrulandi.

## Phase 12 - Direct Multipart-To-Job Streaming

Status: done.

Implemented:

- `POST /api/reconciliations/compare/jobs` artik ASP.NET form-file binding kullanmadan multipart request body'yi dogrudan okuyor.
- Yalnizca bir `branchFile` ve bir `bankFile` kabul ediliyor; eksik, tekrar eden ve beklenmeyen alanlar acik hata kodlariyla reddediliyor.
- Dosya uzantisi stream baslamadan, gercek byte limiti ise kontrollu gecici depoya yazilirken dogrulaniyor.
- Client dosya adi yalnizca guvenli gorunen ad olarak tutuluyor; fiziksel dosya yolu sunucu tarafinda batch kimliginden uretiliyor.
- Bos, gecersiz veya limit asan upload'larda kismi batch klasoru temizleniyor ve history kaydi olusturulmuyor.
- Basarili upload, restart recovery ve background worker cleanup davranislari korunuyor.
- Senkron dosya endpointi geriye uyumluluk icin aynen korunuyor.
- Eksik dosya, limit asimi, beklenmeyen alan, tekrar eden alan, gecersiz content type ve gecersiz uzanti regression testleri eklendi.

## Phase 13 - Persistent Job Leases And Retries

Status: done for the first production-ready contract.

Implemented:

- Database ve uploaded-file background batch'lerine `AttemptCount`, `LastAttemptAt`, `NextAttemptAt`, `LeaseOwner` ve `LeaseExpiresAt` alanlari eklendi.
- PostgreSQL'de queued veya lease suresi dolmus processing isini sahiplenme tek atomik update ile yapiliyor.
- Ayni batch'i iki instance'in ayni anda sahiplenmesi ve eski lease sahibinin sonucu tamamlamasi engelleniyor.
- Worker uzun suren islerde lease'i periyodik yeniliyor; lease kaybinda eski isleme sonucu kaydetmiyor.
- In-process channel yalnizca hizli uyandirma sinyali; kalici queued/expired isler PostgreSQL'den periyodik taraniyor.
- Yerel kanal dolu olsa bile kalici batch kabul ediliyor ve poller tarafindan daha sonra aliniyor.
- Gecici veritabani/worker hatalari yapilandirilabilir deneme limiti ve gecikmeyle yeniden kuyruga aliniyor.
- CSV sema hatasi, duplicate key ve eksik upload gibi kalici hatalar tekrar denenmeden kapatiliyor.
- Retry bekleyen uploaded-file batch'lerinin gecici dosyalari korunuyor; terminal sonuc veya basarida temizleniyor.
- API ve frontend deneme sayisini, son deneme zamanini ve sonraki deneme zamanini gosteriyor.
- Atomik sahiplenme gercek PostgreSQL uzerinde iki eszamanli worker ile dogrulandi.
- Migration gercek yerel PostgreSQL veritabanina uygulandi.
- Masaustu ve mobil UI tarayici ile dogrulandi; mobil gorunumde yatay tasma ve konsol hatasi yok.

## Phase 14 - Shared File-System Storage Affinity

Status: done for shared file systems.

Implemented:

- `ReconciliationUpload:TemporaryStorageMode` icin `Local` ve `SharedFileSystem` modlari eklendi.
- `SharedFileSystem` modu mutlak bir UNC veya mounted-volume yolu gerektiriyor.
- Her gecici depo kokunde atomik olusturulan, kalici ve gizli olmayan bir storage identity marker bulunuyor.
- Ayni ortak koku kullanan instance'lar ayni storage key'i, farkli yerel kokler farkli key'leri aliyor.
- Uploaded-file batch'i PostgreSQL'de storage key ile esleniyor; candidate sorgusu ve atomik claim ayni key'i zorunlu tutuyor.
- Yanlis yerel depoya bagli instance dosyayi sahiplenip `UploadedFileUnavailable` ile hatali kapatamiyor.
- Migration oncesinden kalan ve deposu guvenle belirlenemeyen queued/processing upload isleri acik bir hata koduyla kapatiliyor ve yeniden gonderilmesi gerekiyor.
- Ayni/farkli storage root ve repository affinity davranisi unit testlerle; PostgreSQL claim davranisi gercek veritabaniyla dogrulandi.
- Gercek PostgreSQL suite'inde gorulen, aktif data reader kapanmadan read-only transaction rollback edilmesi sorunu da duzeltildi.
- Migration gercek yerel PostgreSQL veritabanina uygulandi.

## Phase 15 - Temporary File Retention And Orphan Cleanup

Status: done.

Implemented:

- Gecici dosya retention suresi, cleanup araligi ve tarama batch boyutu `ReconciliationUpload` altinda yapilandirilabilir hale getirildi.
- Background cleanup servisi uygulama baslangicinda ve belirlenen araliklarla calisiyor.
- Yalnizca retention suresini asan, batch kimligi formatindaki dogrudan alt klasorler aday olarak aliniyor.
- Aday kimlikler tek sorguda PostgreSQL'e gonderiliyor; ayni storage key'e bagli `Queued` ve `Processing` upload isleri silinmeye karsi korunuyor.
- Retry bekleyen queued isler ve lease sahibi processing isler dosya yasindan bagimsiz olarak korunuyor.
- History kaydi olusmadan kalan eski upload klasorleri ile terminal cleanup kalintilari sinirli batch'ler halinde temizleniyor.
- Ortak depoda birden cok cleanup worker'in ayni klasoru hedeflemesi idempotent; basarisiz silme sonraki taramada yeniden deneniyor.
- Reparse-point ve batch kimligi olmayan klasorler tarama disinda birakiliyor.
- Eski/yeni klasor ayrimi, aktif is korumasi, yetim ve terminal dosya temizligi unit testlerle; aktif is sorgusu gercek PostgreSQL ile dogrulandi.

## Phase 16 - S3-Compatible Object Storage

Status: done for AWS S3 and MinIO-compatible endpoints.

Implemented:

- `TemporaryStorageMode` icin `S3Compatible` secenegi eklendi.
- AWS SDK for .NET v4 `AWSSDK.S3` paketiyle AWS S3 ve custom endpoint/path-style MinIO destegi eklendi.
- Bucket, prefix, region, service URL ve path-style ayarlari startup validation kapsaminda.
- Credential degerleri uygulama ayarina eklenmedi; AWS default credential chain ve standart ortam degiskenleri kullaniliyor.
- Dosya-store kontrati ag I/O'sunu bloklamamak icin tamamen async hale getirildi.
- Branch/bank nesne anahtarlari yalnizca server-side prefix, batch kimligi ve sabit dosya adindan uretiliyor.
- Upload stream'i SDK tarafindan okunurken gercek byte limiti uygulanmaya devam ediyor.
- Open, head/exists, toplu delete ve sayfali list islemleri provider adapter arkasina alindi.
- S3 storage affinity; endpoint, region, bucket ve prefix bilgisinin deterministik hash'iyle uretiliyor.
- Retention servisi S3 nesnelerini batch kimligine gore grupluyor ve en yeni object timestamp'ini kullanarak aktif is korumasini devam ettiriyor.
- S3/MinIO yapilandirmasi service URL icinde credential kabul etmiyor.
- In-memory object client ile upload/read/delete, limit, object key, storage identity ve retention testleri eklendi.
- Gercek provider testi bucket ortam degiskeni verildiginde calisan kosullu integration test olarak eklendi.

## Phase 17 - Dependency Readiness

Status: done.

Implemented:

- Mevcut `/api/health` yalnizca process liveness sonucu olarak korundu.
- Yeni `/api/health/ready` endpoint'i PostgreSQL ve secili gecici depoyu kontrol ediyor.
- PostgreSQL yapilandirilmissa `CanConnectAsync`, test/in-memory modunda ise mevcut repository kontrati kullaniliyor.
- Local ve shared-file-system depolari benzersiz probe dosyasi ile write/read/delete yetkisini dogruluyor.
- S3/MinIO readiness kontrolu credential ve bucket/prefix erisimini tek, prefix-scoped list istegiyle dogruluyor.
- Tum dependency kontrolleri ortak, yapilandirilabilir timeout icinde paralel calisiyor.
- Basarili readiness `200 Ready`, herhangi bir dependency hatasi `503 NotReady` donuyor.
- Response yalnizca dependency bazinda `Ready`/`Unavailable` bilgisi iceriyor; exception veya credential ayrintisi sizdirilmiyor.
- Storage arizalansa bile liveness endpoint'inin `200 Running` kalmasi regression testiyle korundu.
- Filesystem probe cleanup'i, S3 prefix/max-key kontrati, 200/503 response ve hassas hata metni sizintisi test edildi.
- PostgreSQL readiness gercek yerel veritabaniyla calisan kosullu integration testiyle dogrulandi.

## Phase 18 - S3/MinIO Security And Lifecycle Infrastructure

Status: done for reusable deployment contracts; target environment deployment is operator-owned.

Implemented:

- Uygulama upload istekleri icin `BucketDefault`, `AES256` ve `AwsKms` server-side encryption modlari eklendi.
- `AwsKms` modu KMS key kimligini zorunlu tutuyor; diger modlarda yanlislikla key verilmesi startup validation ile reddediliyor.
- AWS bucket isteklerine opsiyonel 12 haneli `ExpectedBucketOwner` korumasi eklendi.
- Put, get, head, delete ve list isteklerinin owner/encryption kontrati unit testlerle dogrulandi.
- AWS CloudFormation sablonu default SSE-S3, versioning, public-access block, bucket-owner-enforced ownership ve TLS zorunlulugu ile eklendi.
- AWS workload rolunun yetkileri yalnizca secilen prefix icin `List`, `Get`, `Put` ve `Delete` islemleriyle sinirlandi.
- AWS lifecycle kurali current/noncurrent object temizligi ve yarim kalmis multipart upload temizligi sagliyor.
- MinIO icin ayni prefix-sinirli PBAC policy sablonu ile operator versioning, lifecycle ve KMS/SSE-S3 kurulum adimlari eklendi.
- Uygulama kimligine lifecycle, encryption, policy veya bucket yonetim yetkisi verilmedi.
- Altyapi JSON sablonlarinin encryption, versioning, lifecycle ve least-privilege kapsamlarini koruyan regression testleri eklendi.
- Provider lifecycle'in PostgreSQL aktif is korumasini bilmedigi belgelenerek varsayilan 30 gunluk safety-net retention secildi.

## Phase 19 - Audit Retention And Archive

Status: done for database-backed retention; external immutable storage remains optional.

Implemented:

- Audit kayitlari icin hot retention, archive retention, cleanup araligi ve batch boyutu config'e tasindi.
- Varsayilan 365 gunluk hot kayitlar atomik PostgreSQL transaction'i ile ayri archive tablosuna tasiniyor.
- Arsiv kayitlari mevcut audit API filtreleri ve sayfalamasinda gorunmeye devam ediyor.
- Varsayilan yedi yillik arsiv retention suresi sonunda sinirli batch'lerle purge yapiliyor; `null` ile suresiz saklama destekleniyor.
- Coklu instance arsivleme islemleri PostgreSQL `FOR UPDATE SKIP LOCKED` ile cakismadan calisiyor.
- Her arsiv kaydi icin SHA-256 icerik ozeti saklaniyor ve okuma sirasinda butunluk kontrol ediliyor.
- Hash kontrolunun dijital imza veya WORM garantisi olmadigi dokumante edildi.
- Migration, in-memory davranis, config validation, hash ve kosullu gercek PostgreSQL testi eklendi.

## Phase 20 - Immutable External Audit Archive

Status: done for the optional S3 Object Lock contract; real-provider compliance profile remains next.

Implemented:

- Harici arsiv varsayilan olarak kapali ve ayri S3-compatible bucket/prefix ayarlariyla etkinlestiriliyor.
- Arsiv batch'leri deterministik JSON, payload SHA-256 ve en az 32 byte anahtarli HMAC-SHA256 etiketiyle uretiliyor.
- S3 upload istegi tam nesne SHA-256 checksum, `COMPLIANCE` Object Lock ve retain-until tarihi tasiyor.
- Yazilan nesne metadata uzerinden hash, lock modu ve retention tarihiyle dogrulanmadan PostgreSQL satiri aktarilmis sayilmiyor.
- Harici aktarim etkinse yalnizca basarili WORM nesne anahtari kaydedilmis arsiv satirlari purge edilebiliyor.
- Retry ayni payload icin ayni object key'i uretiyor ve var olan nesnenin lock/hash kontratini dogruluyor.
- Signing key appsettings'e konmuyor; secret-aware configuration ve key id rotasyonu dokumante edildi.
- AWS icin ayri Object Lock bucket CloudFormation sablonu; MinIO icin ayri with-lock bucket/prefix policy kontrati eklendi.
- Uygulama kimligine delete, retention bypass, lifecycle veya bucket yonetim yetkisi verilmiyor.
- HMAC'in asimetrik non-repudiation saglamadigi acikca belgelendi.

## Phase 21 - Real MinIO WORM CI Profile

Status: done in the CI contract; first remote runner execution remains externally observable.

Implemented:

- CI MinIO ortaminda temporary upload bucket'indan ayri `--with-lock` audit bucket'i olusturuluyor.
- Bucket default retention modu `COMPLIANCE` ve suresi 3650 gun olarak ayarlaniyor.
- Prefix-sinirli audit policy ayni least-privilege uygulama test kimligine ekleniyor.
- Gercek provider testi imzali audit nesnesini MinIO'ya yaziyor ve metadata uzerinden Object Lock modu, retain-until tarihi ve payload hash'ini dogruluyor.
- Uygulama kimligiyle `DeleteObject` isleminin reddedildigi test ediliyor.
- Retention'i bir gune dusurme ve governance bypass girisiminin reddedildigi test ediliyor.
- Ortam degiskeni eksikse CI profilinin sessizce gecmesini engelleyen required guard eklendi.
- Workflow kontrat testi with-lock, COMPLIANCE retention, audit policy ve required profile satirlarini koruyor.

## Phase 22 - Asymmetric Audit Signatures

Status: done for RSA-PSS application signing; managed KMS/HSM signing remains optional.

Implemented:

- Arsiv imza katmani algoritmadan bagimsiz hale getirildi.
- Geriye uyumlu `HmacSha256` ve bagimsiz public-key dogrulamali `RsaPssSha256` secenekleri eklendi.
- RSA-PSS SHA-256 ve en az 2048-bit anahtar zorunlulugu getirildi.
- Private/public PEM anahtarlarinin eslesmesi startup validation sirasinda kriptografik probe ile dogrulaniyor.
- HMAC ve RSA ayarlarinin ayni anda verilmesi reddediliyor.
- Arsiv envelope ve S3 metadata imza algoritmasi ile key id bilgisini tasiyor.
- RSA public key ile dogrulama ve degistirilmis payload reddi regression testiyle korundu.
- Private key'in appsettings'e yazilmamasi ve secret manager uzerinden verilmesi dokumante edildi.

## Phase 23 - AWS KMS Managed Audit Signing

Status: done for the AWS KMS adapter and least-privilege infrastructure contract; real AWS KMS integration remains conditional.

Implemented:

- `AWSSDK.KeyManagementService` v4 projeye sabitlendi.
- `AwsKmsRsaPssSha256` signing secenegi eklendi; local HMAC/RSA davranislari korundu.
- Payload boyutundan bagimsiz calismak icin KMS'e yalnizca SHA-256 digest gonderiliyor.
- KMS istegi `MessageType=DIGEST` ve `RSASSA_PSS_SHA_256` kontratini zorunlu tutuyor.
- KMS imzasi ayni key ile `Verify` edilmeden audit arsiv nesnesi uretilmiyor.
- KMS modunda local HMAC veya PEM private/public key verilmesi startup'ta reddediliyor.
- Agsiz SDK test double ile digest, algoritma, key id, Sign ve Verify akisinin tamamlandigi dogrulandi.
- AWS CloudFormation audit sablonuna retained RSA-3072 `SIGN_VERIFY` KMS key ve alias eklendi.
- Uygulama rolune yalnizca `kms:Sign` ve `kms:Verify` veriliyor; decrypt ve key deletion yetkileri verilmiyor.

## Phase 24 - Conditional Real AWS KMS And WORM Profile

Status: done in code and CI contract; execution requires operator-provided AWS test resources.

Implemented:

- Gercek AWS testi KMS Sign/Verify ve S3 Object Lock yazimini tek uctan uca akista calistiriyor.
- S3 metadata uzerinden `COMPLIANCE`, retain-until, `AWS-KMS-RSA-PSS-SHA256`, key id ve payload hash dogrulaniyor.
- Test rolunun `DeleteObject` yapamadigi negatif test ile kanitlaniyor.
- Profil bucket ve KMS ortam degiskenleri eksikken local suite'i etkilemeden kosullu calisiyor; required modda eksik ayar hata uretiyor.
- GitHub Actions AWS kimligini statik access key yerine OIDC ve kisa omurlu role session ile aliyor.
- Bes AWS repository secret'inin tamami yoksa profil atlanıyor; kismi konfigurasyon CI'i basarisiz yapiyor.
- Repo-sinirli GitHub OIDC trust ve prefix/key-sinirli session policy icin CloudFormation sablonu eklendi.
- CI rolunde S3 delete, KMS decrypt ve key yonetim yetkileri bulunmuyor.
- AWS test nesneleri bir gun COMPLIANCE retention ile yaziliyor; bucket lifecycle daha uzun tutulmali.

## Phase 25 - Audit Retention Operational Status

Status: done.

Implemented:

- Hot tablo, PostgreSQL arsivi ve dis WORM aktarim kuyrugu icin sayaclar eklendi.
- Retention worker'in son baslama, basari ve hata zamani ile son tasima/silme/harici aktarma sayilari process boyunca izleniyor.
- Yonetici yetkili `GET /api/reconciliation-audit-retention/status` endpoint'i `Ready`, `Backlog`, `Degraded` ve `Disabled` durumlarini donuyor.
- Durum cevabi kimlik bilgisi, imza anahtari, bucket ayrintisi veya exception metni icermiyor.
- Yonetim ekraninda aktif kayit, arsiv kaydi, WORM backlog'u ve son basarili calisma sade bir durum kartinda gosteriliyor.
- Endpoint yetkilendirmesi, sayaclar, monitor gecisleri ve frontend kontrati regression testleriyle korunuyor.

## Phase 26 - Audit Retention Metrics And Alert Thresholds

Status: done.

Implemented:

- `BankingReconciliation.AuditRetention` adli standart .NET/OpenTelemetry meter kaynagi eklendi.
- Run sonucu ve suresi; hot, archive ve WORM backlog sayilari; son basarili calismanin yasi dusuk-cardinality metric olarak yayinlaniyor.
- Stabil OpenTelemetry `1.17.0` hosting ve OTLP exporter paketleri sabitlendi.
- OTLP aktarimi varsayilan olarak kapali; yalnizca gecerli mutlak HTTP/HTTPS collector endpoint'i ile etkinlesiyor.
- WORM backlog sayisi, backlog yasi ve worker gecikmesi icin yapilandirilabilir alarm esikleri eklendi.
- Basarisiz run veya asilan esikler yonetim durumunu `Degraded` yapiyor.
- Sanitized `GET /api/health/audit-retention` probe'u kritik durumda `503`, normal/backlog/disabled durumda `200` donuyor.
- Yonetim arayuzu teknik alarm kodlarini kullaniciya anlasilir Turkce mesajlarla gosteriyor.
- Metric adlari/etiketleri, secenek dogrulamasi, backlog alarmlari ve health probe regression testleriyle korunuyor.

## Phase 27 - Production Readiness

Status: repository controls complete; real environment acceptance requires operator-provided infrastructure and identities.

Implemented:

- Staging/Production bos OIDC authority, audience, veritabani, wildcard host, local gecici depolama, kapali telemetry veya guvensiz placeholder parola ile baslamiyor.
- JWT issuer/audience/lifetime/signature zorunlulugu, sinirli clock skew ve yapilandirilabilir claim tipleri eklendi.
- HSTS, guvenilir proxy CIDR'lari, guvenlik header'lari ve IP-bolumlu global rate limit eklendi.
- Base appsettings'ten yerel PostgreSQL parolasi kaldirildi; production secret'lari yalnizca Kubernetes secret referanslariyla aliniyor.
- Non-root/read-only container, pod security context, health probe, PDB, HPA, TLS ingress ve default-deny NetworkPolicy eklendi.
- Web pod'undan otomatik migration kaldirildi; `--migrate-only` tek seferlik deployment Job'i ve pending migration fail-fast kontrolu eklendi.
- Context, OIDC discovery, secret key, placeholder, migration ve rollout kontrolu yapan staging/production deployment script'i eklendi.
- Context, cluster erisimi, Docker, External Secrets, ingress, minimum yetki ve OIDC discovery icin salt-okunur staging preflight script'i eklendi.
- Uygulama revision rollback script'i ve geriye uyumlu migration kurali yazildi; otomatik schema rollback yasaklandi.
- PostgreSQL 16.15 Alpine tabanli alti saatlik custom-format dump, katalog dogrulama, SHA-256 ve KMS sifreli S3 upload kontrati eklendi.
- Yalnizca `_restore_verify` son ekli izole veritabanina geri yukleyen, checksum ve zorunlu tablo sorgusu yapan restore testi eklendi.
- CI'a NuGet vulnerability gate, OWASP ZAP passive baseline ve k6 yuk kabul testi eklendi.
- Eski xUnit v2 zinciri xUnit v3'e tasindi; yerel vulnerability taramasi hem API hem test projesinde sifir bilinen zafiyet raporladi.
- `PRODUCTION_READINESS.md` kod kontrollerini gercek ortam kabul kanitlarindan ayiriyor.

## Mentor Notes Mapped To Project

Implemented from mentor notes:

- Regression tests.
- Excel output.
- Database summary storage.
- Database bloat azaltma.
- Branch/bank source definitions.
- Configurable comparison rules.
- Transaction number mapping.
- Upper/lowercase normalization.
- Whitespace comparison settings.
- Decimal precision settings.
- Branch/bank-specific decimal precision.
- TXT support.
- Validation before reconciliation.
- Useful indexes and unique indexes.
- Avoid storing every raw row.
- User approvals with JWT role/permission authorization.
- Management changes with JWT authorization and before/after audit trail.
- Direct multipart-to-job streaming with actual-byte limits and partial-upload cleanup.
- Persistent job leases, lease renewal, expired-job recovery, bounded retries, and atomic PostgreSQL ownership.
- Shared file-system storage identity and uploaded-job affinity across application instances.
- Retention-aware orphan cleanup that protects active and retry-waiting uploaded-file jobs.
- AWS S3/MinIO-compatible bounded temporary staging and checksum-protected object storage behind an asynchronous provider contract.
- Separate liveness/readiness checks for PostgreSQL and temporary storage dependencies.
- Prefix-scoped AWS S3/MinIO runtime policies, server-side encryption controls, versioning, and provider-native lifecycle infrastructure.

Partly implemented:

- Large-file processing:
  - Dosya boyutu, dosya kayit sayisi ve veritabani kaynak kayit sayisi sinirlari var.
  - Veritabani kaynaklari background job olarak calisabiliyor.
  - Dosya mutabakati guvenli gecici depodan background job olarak calisabiliyor.
  - HTTP multipart body dogrudan kontrollu job deposuna stream ediliyor.
  - Database job'lari coklu instance'ta atomik PostgreSQL lease ile sahipleniliyor.
  - Uploaded-file job'lari ayni lease modelini ve kalici storage affinity'yi kullaniyor; ortak UNC/mounted-volume veya S3/MinIO deposu kullanan node'lar arasinda tasinabiliyor.

Not implemented yet:

- Azure Blob'a ozel object-storage provider'i.
- Research notes from real reconciliation examples.

## Recommended Next Step

Siradaki zorunlu adim kod degisikligi degil, gercek ortam kabuludur: kurum OIDC bilgileri, Kubernetes staging context'i, immutable image registry adresleri, PostgreSQL/S3/KMS ve secret manager kaynaklari saglanmali; deployment runbook'u staging'de calistirilmali; ZAP/k6, backup restore ve rollback kanitlari kaydedilmelidir. Bu kanitlar tamamlanmadan production onayi kapali kalir.

Yerel gelistirme notu (2026-08-20): Docker Desktop `docker-desktop` context'i ve
Kubernetes 1.36.1 node'u hazirdir. External Secrets, nginx ingress ve gercek OIDC
olmadigi icin bu kume production staging yerine yalnizca yerel smoke testleri icin
kullanilmalidir. Development-only local manifest ve deploy script'i ile uygulama
non-root/read-only pod olarak baslatildi; rollout ve `/api/health` smoke testi gecti.

Yeni tamamlanan:

- CI icinde gecici MinIO tenant'i baslatiliyor.
- Prefix-sinirli least-privilege uygulama kimligi otomatik olusturuluyor.
- Upload, head/read, list, delete ve readiness round-trip testi zorunlu kosuluyor.
- Prefix disi yazma ve lifecycle bilgisi okuma girisimlerinin reddedildigi negatif testler kosuluyor.
- Audit hot/archive retention servisi, PostgreSQL archive tablosu ve butunluk hash kontrolu eklendi.
- Opsiyonel S3 Object Lock `COMPLIANCE` arsivi, HMAC authentication ve purge guvenlik kapisi eklendi.

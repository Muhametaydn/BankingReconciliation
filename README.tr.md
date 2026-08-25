# Banka Mutabakat Uygulaması

[![CI](https://github.com/Muhametaydn/BankingReconciliation/actions/workflows/ci.yml/badge.svg)](https://github.com/Muhametaydn/BankingReconciliation/actions/workflows/ci.yml)

[English](README.md) | [Türkçe](README.tr.md)

Şube ve banka hareketlerini karşılaştırmak için geliştirilmiş web uygulaması.
Eksik hareketleri ve tutar/adet farklarını bulur; sonuçların incelenmesini,
Excel raporu alınmasını ve onay kararının kaydedilmesini sağlar.

## Özellikler

- CSV, ayrılmış TXT, sabit genişlikli TXT ve yapılandırılmış veritabanı
  kaynaklarıyla mutabakat yapar.
- Eksik hareket, tekrarlı anahtar, adet/tutar ve ek sayısal alan farklarını
  tespit eder.
- Dosya kolonlarını ve değerlerini işlemden önce doğrular.
- Farkları Excel formatında dışa aktarır.
- PostgreSQL yapılandırıldığında geçmiş, ayarlar, onaylar ve denetim kayıtlarını
  kalıcı olarak saklar.
- Administrator, Operator ve Approver rollerini destekler.
- Uzun işlemleri yeniden deneme destekli arka plan işçisiyle yürütür.

## Teknolojiler

- .NET 8, ASP.NET Core Minimal API
- PostgreSQL, Entity Framework Core
- JWT kimlik doğrulama ve rol bazlı yetkilendirme
- Docker ve Kubernetes
- AWS S3 uyumlu depolama, MinIO test desteği
- xUnit ve GitHub Actions

## Yerelde çalıştırma

.NET 8 SDK gereklidir.

```powershell
git clone https://github.com/Muhametaydn/BankingReconciliation.git
cd BankingReconciliation
dotnet run --project .\BankingReconciliation.Api\BankingReconciliation.Api.csproj
```

Tarayıcıdan `http://localhost:5230` adresini açın.

**Kayıt ol** ile açılan ilk yerel hesap Admin olur. Sonraki hesaplar Operator
olarak açılır; Admin kullanıcı, Kullanıcı ve Rol Yönetimi bölümünden bir hesaba
Approver rolü verebilir.

Örnek dosyalar:
[`BankingReconciliation.Api/Samples`](BankingReconciliation.Api/Samples)

## Docker ile çalıştırma

```powershell
docker build -t banking-reconciliation:local .
docker run --rm -p 8080:8080 banking-reconciliation:local
```

Ardından `http://localhost:8080` adresini açın.

## Test

```powershell
dotnet test .\BankingReconciliation.sln --configuration Release
dotnet format .\BankingReconciliation.sln --verify-no-changes --no-restore
```

## Ana akış

1. Operator iki kaynağı yükler ve karşılaştırmayı başlatır.
2. Uygulama verileri doğrular ve mutabakatı çalıştırır.
3. Farklar ekrandan incelenir veya Excel olarak indirilir.
4. Approver tamamlanan mutabakatı onaylar ya da reddeder.
5. Onay kararları ve yönetim değişiklikleri denetim kaydına yazılır.

Yerel Swagger adresi: `http://localhost:5230/swagger`.

## Yerel Kubernetes

Docker Desktop Kubernetes için:

```powershell
.\deploy\kubernetes\deploy-local.ps1
```

Bu profil geliştirme amaçlıdır. Dağıtım, yedekleme ve geri alma betikleri için
[Kubernetes rehberine](deploy/kubernetes/README.md) bakın.

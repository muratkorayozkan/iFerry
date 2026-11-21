# iFerry

iPhone’dan Windows’a Wi-Fi üzerinden dosya aktarımı için WPF uygulaması.

- **Tarayıcıdan aktarım** için iPhone’un kişisel erişim noktasına bağlanın, iFerry’de oturum başlatın ve QR kodu iPhone kamerasıyla okutun. Safari’den dosyaları seçip gönderin. Yerel HTTP oturumu kapanana kadar geçici bağlantıyı kullanabilirsiniz.
- **SMB** için paylaşımı yapılandırın ve bir aktarım hesabı oluşturun. iPhone’da **Dosyalar → Göz At → Sunucuya Bağlan** üzerinden uygulamadaki adresi ve aktarım hesabını kullanın. İlk kurulum Windows yönetici onayı ister.
- **AirDrop** seçeneği geliştirme aşamasında.

Gelen dosyalar Belgeler altındaki `iFerry` klasörüne kaydedilir. Dosya adına tıklayarak Windows’un varsayılan uygulamasında açabilirsiniz.

## Derleme ve çalıştırma

Windows üzerinde .NET 10 SDK ile repo kökünden çalıştırın.

```powershell
dotnet run --project src/iFerry.csproj
```

Kaynak kodundan bir dağıtım klasörü oluşturmak için aşağıdaki komutu kullanın.

```powershell
dotnet publish src/iFerry.csproj -c Release --self-contained false -o artifacts/iFerry
```

Dağıtım klasöründeki `iFerry.exe`, .NET 10 Desktop Runtime ve ASP.NET Core Runtime gerektirir. Klasördeki kurulum betiklerini uygulamanın yanında tutun.

## Uygulama güncellemeleri

iFerry açılışta herkese açık GitHub Releases kaynağını denetler. Yeni kararlı sürümü arka planda indirir ve doğrular. **Yeniden başlat ve güncelle** seçeneğiyle kurulumu onaylayabilirsiniz. Kurulum dizini yazılabilir olmalıdır.

Yeni sürümler için proje sürümü, GitHub etiketi ve ZIP adı eşleşmelidir. Örneğin `0.1.1`, `v0.1.1` ve `iFerry-v0.1.1-Windows-x64.zip`. ZIP köküne yayın klasöründeki sekiz uygulama dosyasını ekleyin. PDB dosyasını dışarıda bırakın. GitHub varlık API yüklendi durumunu ve SHA-256 özetini sağlamalıdır.

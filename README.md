# iFerry

iPhone’dan Windows’a Wi-Fi üzerinden dosya aktarımı için WPF uygulaması.

- **Tarayıcıdan aktarım** için iPhone’un kişisel erişim noktasına bağlanın, iFerry’de oturum başlatın ve QR kodu iPhone kamerasıyla okutun. Safari’den dosyaları seçip gönderin. Yerel HTTP oturumu kapanana kadar geçici bağlantıyı kullanabilirsiniz.
- **SMB** için paylaşımı yapılandırın ve bir aktarım hesabı oluşturun. iPhone’da **Dosyalar → Göz At → Sunucuya Bağlan** üzerinden uygulamadaki adresi ve aktarım hesabını kullanın. İlk kurulum Windows yönetici onayı ister.
- **AirDrop** seçeneği geliştirme aşamasında.

Gelen dosyalar Belgeler altındaki `iPhoneAktarim` klasörüne kaydedilir. Dosya adına tıklayarak Windows’un varsayılan uygulamasında açabilirsiniz.

## Derleme ve çalıştırma

Windows üzerinde .NET 10 SDK ile repo kökünden çalıştırın.

```powershell
dotnet run --project Aktarim/Aktarim.csproj
```

Kaynak kodundan bir dağıtım klasörü oluşturmak için aşağıdaki komutu kullanın.

```powershell
dotnet publish Aktarim/Aktarim.csproj -c Release --self-contained false -o artifacts/iFerry
```

Dağıtım klasöründeki `iFerry.exe`, .NET 10 Desktop Runtime ve ASP.NET Core Runtime gerektirir. Klasördeki kurulum betiklerini uygulamanın yanında tutun.

# LidarViewer

Aplikacja na studia dla Windows do oglądania i podstawowej analizy chmur punktów
z Velodyne VLP-16 oraz statycznych plików XYZ, PLY i PCD. Projekt rozwijamy w C#
na .NET 10, z interfejsem WPF i renderowaniem HelixToolkit.Wpf.SharpDX.

Współrzędne są interpretowane w metrach. Początek układu (0,0,0) odpowiada pozycji
LiDAR-u, a oś Z jest pionowa. Pliki statyczne zachowują własne współrzędne,
dlatego znaczenie pomiarów zależy od układu i jednostek pliku.

## Technologie i biblioteki

### Podstawowy stos

| Technologia           | Zastosowanie                                                                                             |
| --------------------- | -------------------------------------------------------------------------------------------------------- |
| C#                    | Modele danych, dekodowanie, import i eksport, filtry oraz logika okna.                                   |
| .NET 10 SDK i runtime | Kompilacja, uruchomienie, zadania asynchroniczne, obsługa plików i sieci. Core i testy mają cel net10.0. |
| WPF                   | Interfejs aplikacji Windows. Desktop i Visualization mają cel net10.0-windows oraz UseWPF.               |
| XAML                  | Deklaracja okna, przycisków, paneli, kontrolek i sceny 3D.                                               |
| DirectX 11            | Renderowanie sceny przez bibliotekę HelixToolkit i sterownik GPU.                                        |
| Visual Studio 2026    | Edycja rozwiązania .slnx, uruchamianie i debugowanie WPF, przegląd testów.                               |
| NuGet                 | Przywracanie pakietów zadeklarowanych w plikach .csproj.                                                 |
| Git i GitHub          | Kontrola wersji i współpraca zespołu. GitHub nie jest wymagany do lokalnego uruchomienia.                |
| PowerShell            | Polecenia budowania oraz lokalny nadajnik testowych pakietów LIVE.                                       |

### Pakiety dodane bezpośrednio do projektów

Wersje poniżej pochodzą z aktualnych plików .csproj.

| Pakiet                    | Wersja  | Projekt i rola                                                                                                        |
| ------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------- |
| HelixToolkit.Wpf.SharpDX  | 3.1.2   | Visualization. Kontrolka Viewport3DX, kamery perspektywiczna i ortograficzna, chmury punktów, linie oraz hit-testing. |
| xunit                     | 2.9.3   | Core.Tests. Testy jednostkowe i integracyjne, asercje i przypadki parametryzowane.                                    |
| xunit.runner.visualstudio | 3.1.5   | Core.Tests. Uruchamianie testów w Visual Studio i przez platformę testową.                                            |
| Microsoft.NET.Test.Sdk    | 17.14.1 | Core.Tests. Integracja projektu testowego z dotnet test i narzędziami Visual Studio.                                  |

HelixToolkit pozwala wykorzystać gotowy renderer, kamery i wskazywanie punktów.
Dane są przekazywane jako PointGeometry3D, a pomocnicze okręgi i pomiar jako
LineGeometry3D. Kolekcje Vector3Collection i Color4Collection przechowują pozycje
oraz kolory w formacie biblioteki. Informacje o projekcie i licencjach są dostępne
w [repozytorium HelixToolkit](https://github.com/helix-toolkit/helix-toolkit).

### Zależności pośrednie renderowania

Te pakiety są pobierane automatycznie wraz z HelixToolkit.
Ich obecność nie oznacza, że nasza aplikacja bezpośrednio używa każdego z ich API.

| Pakiety                                                                                              | Wersja | Rola                                                                                                               |
| ---------------------------------------------------------------------------------------------------- | ------ | ------------------------------------------------------------------------------------------------------------------ |
| HelixToolkit, HelixToolkit.Geometry, HelixToolkit.Maths, HelixToolkit.SharpDX                        | 3.1.2  | Kolekcje, geometria, matematyka i silnik renderowania używany przez warstwę WPF.                                   |
| SharpDX, SharpDX.D3DCompiler, SharpDX.Direct2D1, SharpDX.Direct3D11, SharpDX.Direct3D9, SharpDX.DXGI | 4.2.0  | Dostęp stosu renderowania do API DirectX, kompilatora shaderów i infrastruktury grafiki.                           |
| CommunityToolkit.Common, CommunityToolkit.Diagnostics, CommunityToolkit.Mvvm                         | 8.3.2  | Narzędzia pomocnicze używane przez zależności. Nasze okno korzysta z prostych metod obsługi zdarzeń w code-behind. |
| Cyotek.Drawing.BitmapFont                                                                            | 2.0.4  | Obsługa fontów bitmapowych w stosie graficznym.                                                                    |
| Microsoft.Extensions.DependencyInjection.Abstractions, Microsoft.Extensions.Logging.Abstractions     | 8.0.2  | Kontrakty usług i logowania wymagane przez zależności.                                                             |
| Microsoft.CodeAnalysis.Common, Microsoft.CodeAnalysis.CSharp                                         | 4.11.0 | Infrastruktura Roslyn dostarczana przez graf zależności.                                                           |
| Microsoft.CodeAnalysis.Analyzers                                                                     | 3.3.4  | Analizatory kodu w grafie zależności.                                                                              |
| NETStandard.Library                                                                                  | 1.6.1  | Zależność zgodności starszych pakietów.                                                                            |
| Microsoft.NETCore.Platforms                                                                          | 1.1.0  | Metadane platform wymagane przez zależności.                                                                       |

### Zależności pośrednie testów

| Pakiety                                                                                     | Wersja  | Rola                                                                                                     |
| ------------------------------------------------------------------------------------------- | ------- | -------------------------------------------------------------------------------------------------------- |
| xunit.assert, xunit.core, xunit.extensibility.core, xunit.extensibility.execution           | 2.9.3   | Asercje i mechanizm wykonywania testów xUnit.                                                            |
| xunit.abstractions                                                                          | 2.0.3   | Kontrakty infrastruktury testowej.                                                                       |
| xunit.analyzers                                                                             | 1.18.0  | Analiza poprawnego użycia xUnit podczas kompilacji.                                                      |
| Microsoft.CodeCoverage, Microsoft.TestPlatform.ObjectModel, Microsoft.TestPlatform.TestHost | 17.14.1 | Infrastruktura testów i narzędzia pokrycia kodu. Samo uruchomienie testów nie generuje raportu pokrycia. |
| Newtonsoft.Json                                                                             | 13.0.3  | Zależność infrastruktury testowej.                                                                       |

Pełną listę zależności po przywróceniu pakietów można sprawdzić poleceniem:

```powershell
dotnet list LidarViewer.slnx package --include-transitive
```

Dokładny rozwiązany graf dla danego projektu znajduje się też w generowanym pliku
obj/project.assets.json. Zależności pośrednie mogą zmienić się po aktualizacji pakietów.

### Używane elementy biblioteki standardowej .NET

| API / przestrzeń nazw                                  | Zastosowanie                                                                                   |
| ------------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| System.Net i System.Net.Sockets / UdpClient            | Adresy IP, odbiór datagramów z czujnika i testy lokalnej transmisji.                           |
| System.Net.NetworkInformation                          | Lista lokalnych adresów IPv4 do wyboru w panelu Live.                                          |
| System.Threading.Channels                              | Ograniczona do jednej gotowej klatki kolejka LIVE, pozwalająca zastąpić zaległą chmurę nowszą. |
| Task, async/await, CancellationToken, IAsyncEnumerable | Praca w tle, anulowanie odczytu i asynchroniczne dostarczanie chmur.                           |
| SemaphoreSlim                                          | Budzenie i wstrzymywanie istniejącej pętli odtwarzania PCAP.                                   |
| Interlocked i Volatile                                 | Bezpieczne liczniki i stan odbiornika współdzielone między zadaniami.                          |
| PeriodicTimer                                          | Odstępy między chmurami syntetycznej symulacji.                                                |
| System.Diagnostics.Stopwatch                           | Pomiar upływu czasu i synchronizacja tempa odtwarzania.                                        |
| System.Buffers.Binary / BinaryPrimitives               | Odczyt pól binarnych z poprawnym porządkiem bajtów.                                            |
| System.IO                                              | Odczyt PCAP i chmur, indeksowane skoki w pliku oraz eksport.                                   |
| System.Globalization / InvariantCulture                | Jednoznaczny zapis i odczyt liczb w plikach, niezależnie od ustawień regionalnych.             |
| System.Numerics                                        | Wektory używane w geometrii i obliczeniach kolorów.                                            |
| Microsoft.Win32 / OpenFileDialog i SaveFileDialog      | Standardowe okna wyboru pliku w aplikacji WPF.                                                 |
| Dispatcher i DispatcherTimer                           | Aktualizacja kontrolek na wątku WPF i okresowe odświeżanie diagnostyki Live.                   |

## Dostępne funkcje

- Syntetyczna scena domku z podłożem, zawierająca 13 162 punkty, oraz Start/Stop symulacji.
- Import statycznych chmur XYZ, PLY i PCD.
- Odtwarzanie PCAP VLP-16, pauza, wznowienie, suwak czasu i nawigacja po klatkach.
- Szybkości PCAP 0,5×, 1×, 2× i 4×.
- Odbiór Live z VLP-16 przez Ethernet/UDP.
- Kamera 3D oraz ortograficzne widoki XY, XZ i YZ.
- Kolorowanie według odległości, wysokości Z, intensywności, RGB lub jednolicie na niebiesko.
- Niezależne filtry odległości, wysokości i intensywności, łączone warunkiem AND.
- Pomiar XYZ i odległości od początku układu, znacznik punktu i linia pomiarowa.
- Przybliżone śledzenie pomiaru między klatkami przez najbliższy punkt do 2 m od poprzedniej pozycji.
- Pomocnicze okręgi XY z odstępami 1, 5 lub 10 m, domyślnie wyłączone.
- Eksport aktualnej widocznej chmury do PLY ASCII, PCD ASCII lub XYZ.

## Formaty i dane

| Format | Odczyt                                                                                                                               | Zapis                                                        |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------ |
| XYZ    | Tekstowe XYZ, opcjonalna intensywność i RGB. Separatory to spacje lub tabulatory, komentarze zaczynają się od #.                     | Tekst, kolejność XYZ, następnie dostępna intensywność i RGB. |
| PLY    | ASCII i binary little-endian 1.0, wierzchołki XYZ oraz opcjonalna intensywność i RGB.                                                | ASCII 1.0.                                                   |
| PCD    | ASCII i binary bez kompresji, pola XYZ i opcjonalna intensywność oraz RGB.                                                           | ASCII, z odpowiednim nagłówkiem pól.                         |
| PCAP   | Klasyczny PCAP 2.4, Ethernet, IPv4, pakiety pomiarowe VLP-16. Obsługiwane są nagłówki little/big-endian i czas mikro-/nanosekundowy. | Eksport pojedynczej klatki odbywa się do pliku statycznego.  |

Model Point3D zawiera X, Y, Z typu float, intensywność typu byte,
HasIntensity i opcjonalny kolor Rgb. Brak intensywności w importowanym pliku
jest rozróżniany od rzeczywistej wartości zero. Wartości intensywności są
dopasowywane do zakresu obecnego modelu 0–255.

PCAP i Live współdzielą Vlp16PacketDecoder. Dekoder zachowuje obecną skalę
odległości 2 mm, kąty 16 wiązek i interpolację azymutu. Obsługuje single strongest,
last return i dual return. Przyszłe dokładniejsze korekty kalibracyjne wymagają
osobnej weryfikacji.

## Architektura i ważne pliki

```text
LidarViewer.slnx
Directory.Build.props
src/
  LidarViewer.Core/
    Point3D.cs
    IPointSource.cs
    SyntheticPointSource.cs
    Vlp16PacketDecoder.cs
    Vlp16PcapReader.cs
    PcapRecording.cs
    Vlp16LiveReceiver.cs
    PointCloudFileReader.cs
    PointCloudFileWriter.cs
    PointCloudFilter.cs
    PointMeasurement.cs
  LidarViewer.Visualization/
    PointCloudView.xaml
    PointCloudView.xaml.cs
    PointColoring.cs
    DistanceRings.cs
  LidarViewer.Desktop/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    PlaybackClock.cs
tests/
  LidarViewer.Core.Tests/
samples/
  domek.xyz
  domek.ply
  domek.pcd
tools/
  Send-LiveTest.ps1
docs/
  LIVE.md
  PROJECT_PLAN.md
  RESPONSIBILITIES.md
```

Core pozostaje niezależny od WPF. Visualization korzysta z Core i HelixToolkit.
Desktop integruje moduły oraz obsługuje zdarzenia kontrolek w code-behind.
IPointSource jest używany przez generator syntetyczny. PCAP i Live mają
bezpośrednie mechanizmy odczytu dopasowane do ich sposobu działania.

- **PcapRecording** tworzy indeks pozycji pakietów, numerów bloków i czasów.
  Przewijanie odczytuje potrzebną klatkę zamiast całego nagrania.
- **Vlp16LiveReceiver** odbiera i weryfikuje pakiety, składa klatki oraz udostępnia
  najnowszą kompletną chmurę do wyświetlenia.
- **PointCloudFileReader / Writer** obsługują statyczne formaty i eksport.
- **PointCloudFilter** przetwarza oryginalną chmurę jednym przebiegiem.
- **PointCloudView** rysuje chmurę, okręgi i pomiar oraz obsługuje kamery i hit-testing.
- **MainWindow** steruje źródłami, zachowuje oryginalne punkty i przekazuje wynik
  filtrów do widoku. Eksport pobiera kopię faktycznie wyświetlonych punktów.
- **PlaybackClock** wyznacza pozycję czasu nagrania przy wybranej szybkości,
  uwzględniając pauzę i zmianę tempa.

Odczyt, filtrowanie i przygotowanie geometrii korzystają z zadań w tle.
Gotowa geometria jest przypisywana na wątku WPF. Zapis utrwala kopię chmury
z chwili kliknięcia przycisku, dzięki czemu kolejne klatki jej nie zmieniają.

Directory.Build.props włącza nullable reference types, implicit usings
oraz traktowanie ostrzeżeń kompilatora jako błędów.

## Wymagania środowiska

- Windows z .NET 10 i obsługą DirectX 11.
- .NET 10 SDK do kompilacji, wraz z pakietami wymaganymi przez WPF.
- Visual Studio 2026 z obciążeniem „Programowanie aplikacji klasycznych dla platformy .NET”.
- Dostęp do źródła NuGet podczas pierwszego przywrócenia pakietów.
- Dla fizycznego Live: karta Ethernet, zasilony VLP-16 i prawidłowa konfiguracja sieci.

## Budowanie i uruchomienie

W Visual Studio otwórz LidarViewer.slnx, przywróć pakiety i ustaw
LidarViewer.Desktop jako projekt startowy. Naciśnij F5.

Z PowerShell w głównym katalogu projektu:

```powershell
dotnet restore LidarViewer.slnx
dotnet build LidarViewer.slnx -c Release --no-restore
dotnet test LidarViewer.slnx -c Release --no-build
dotnet run --project src/LidarViewer.Desktop -c Release --no-build
```

Po uruchomieniu demo automatycznie otwiera samples/demo.pcap — około
10-sekundowy fragment nagrania Depot 10Hz Single Returns z VLP-16.
Próbka jest częścią repozytorium i ma rozmiar poniżej limitu GitHub.
Projekt kopiuje ją do katalogu aplikacji podczas budowania oraz publikowania.
Ścieżka jest wyznaczana względem AppContext.BaseDirectory, dzięki czemu działa
po pobraniu projektu do dowolnego folderu. Pełne nagranie źródłowe pozostaje
w oryginalnej lokalizacji poza repozytorium demo.

## Obsługa

### Pliki i odtwarzanie

Dolny pasek zawiera „Otwórz plik”, eksport, oś czasu, przyciski poprzedniej
i następnej klatki, „Pauza/Start” oraz wybór szybkości.

Przewijanie i nawigacja po klatkach wstrzymują odtwarzanie. Wznowienie odbywa się
przyciskiem „Pauza/Start”. Suwak wybiera klatkę o najbliższym czasie początku,
a przy remisie wcześniejszą. Czas na osi dotyczy oryginalnego nagrania.
Nieprawidłowe lub cofające się znaczniki mogą być korygowane podczas indeksowania,
co ogranicza dokładność czasową takich fragmentów.

Przycisk „Zrób zrzut do pliku .ply” otwiera okno zapisu. PLY jest formatem
domyślnym, ale okno pozwala też wybrać PCD lub XYZ. Zapis obejmuje widoczne punkty
po filtrach i atrybuty danych, z pominięciem geometrii pomocniczej.
Kolory odległości lub wysokości wyliczane do wyświetlania nie zastępują
oryginalnego RGB w eksportowanym pliku.

### Kamera, pomiar i filtry

- Prawy przycisk myszy obraca kamerę 3D.
- Shift + prawy przycisk przesuwa widok, kółko przybliża i oddala.
- W widokach 2D obrót jest wyłączony, dostępne są przesuwanie i zoom.
- „Włącz pomiar” pozwala wskazać punkt lewym przyciskiem myszy.
- Po zmianie pozycji PCAP pomiar jest czyszczony.
- Pola filtrów przyjmują kropkę lub przecinek dziesiętny. Przy niepełnym albo
  błędnym wpisie pozostaje ostatni poprawny zestaw ustawień.
- Filtr intensywności pomija punkty bez dostępnej intensywności.
- Zakres kolorowania wysokości ustala się na podstawie pełnej chmury źródłowej,
  a w PCAP i Live pierwszej klatki. Filtry nie zmieniają tej skali.
- Okręgi leżą w XY przy Z=0. Limit wynosi 100 okręgów, co ogranicza ich zasięg
  do 100, 500 lub 1000 m zależnie od wybranego odstępu.

### Live i lokalny test UDP

Wybierz lokalny IPv4 lub 0.0.0.0, ustaw port i kliknij „Połącz Live”.
Status „Nasłuchiwanie” potwierdza otwarcie portu. „Odbieranie danych” oznacza
odebranie poprawnego pakietu VLP-16. Po 3 sekundach bez poprawnych pakietów
pojawia się „Brak danych”.

Do testu bez czujnika wybierz 127.0.0.1 i port 2368, a następnie uruchom:

```powershell
./tools/Send-LiveTest.ps1 -TargetAddress 127.0.0.1 -Port 2368
```

Syntetyczny nadajnik tworzy pomiary w odległości około 10 m.
Przycisk „Rozłącz” zwalnia port i pozostawia ostatnią chmurę.
Zmiana źródła kończy poprzednie odtwarzanie lub odbiór.

Instrukcję konfiguracji Ethernetu i fizycznego urządzenia opisuje
[docs/LIVE.md](docs/LIVE.md).

## Testy i stan weryfikacji

Testy xUnit obejmują generator, dekodowanie PCAP i Live, indeksowanie i czasy
klatek, losowy odczyt, zegar odtwarzania, import ASCII i binarny, eksport z ponownym
odczytem, filtry, pomiar, kolory, geometrię okręgów oraz lokalny odbiór UDP.

Czysta logika geometrii, kolorów i zegara jest dołączona do projektu testowego
przez elementy Compile Link. Dzięki temu testy sprawdzają te same pliki źródłowe
bez tworzenia okna WPF.

Ostatnia wykonana pełna weryfikacja zakończyła się kompilacją Release bez błędów
i ostrzeżeń oraz wynikiem **126/126 testów zaliczonych**. Późniejsze poprawki
napisów w interfejsie nie obejmowały ponownego uruchamiania testów.

Odbiornik Live sprawdzono na lokalnych datagramach. Nie przeprowadzono testu na
fizycznym VLP-16. Ostatnie zmiany interfejsu wymagają sprawdzenia ręcznego.

Przykładowa weryfikacja GUI:

1. Uruchom domek z panelu Symulacja, sprawdź Start/Stop i 13 162 punkty.
2. Otwórz pliki samples/domek.xyz, domek.ply i domek.pcd, każdy po 1730 punktów.
3. Zmień widoki 3D/XY/XZ/YZ, kolorowanie, filtry i widoczność okręgów.
4. Wskaż punkt pomiarowy i sprawdź współrzędne oraz linię do początku układu.
5. Zapisz chmurę w każdym formacie i otwórz ją ponownie.
6. W PCAP sprawdź pauzę, przewijanie, nawigację i szybkości 0,5×–4×.
7. W Live użyj lokalnego nadajnika, rozłącz i połącz ponownie.
8. Zamknij aplikację podczas działania źródła, sprawdź ponowne uruchomienie.

## Ograniczenia i dalszy rozwój

- Obsługiwane są podane warianty formatów. PLY big-endian, PCD binary_compressed
  oraz PCAPNG wymagają osobnej implementacji.
- Częściowo brakujące RGB lub intensywność można zachować przy eksporcie do XYZ.
  Dla PLY i PCD wymagane są wspólne atrybuty w całej eksportowanej chmurze.
- Pomiar zakłada metry i początek układu w LiDAR-ze.
- Śledzenie najbliższego punktu może przejść na sąsiednią powierzchnię.
- W Live system lub sieć mogą utracić datagramy. Kolejka wyświetlania pomija
  jedynie gotowe zaległe klatki.
- Przy wolniejszym sprzęcie rzeczywista szybkość odtwarzania może być mniejsza
  od zadanej. Nie jest tworzona nieograniczona kolejka klatek.
- Weryfikacja na czujniku i porównanie z VeloView pozostają zadaniem sprzętowym.

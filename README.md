# LidarViewer

LidarViewer to aplikacja na Windows do oglądania chmur punktów z LiDAR-u
Velodyne VLP-16. Można otworzyć nagranie PCAP, przejrzeć skany w 3D lub 2D,
zmierzyć odległość od czujnika i odfiltrować część punktów. Jest też odbiór
na żywo i zapis surowych pomiarów do PCAP. Projekt powstał na zajęcia na studiach.

Współrzędne traktujemy jako metry, oś Z jest pionowa, a LiDAR znajduje się
w (0,0,0). Przy plikach statycznych trzeba sprawdzić ich jednostki i układ odniesienia.

## Technologie i struktura

Kod jest napisany w C# na .NET 10. Interfejs korzysta z WPF i XAML,
a widok chmury z HelixToolkit.Wpf.SharpDX 3.1.2 oraz DirectX 11.
HelixToolkit pobiera m.in. SharpDX 4.2.0, własne moduły matematyczne i geometrii
oraz CommunityToolkit. Nie dodawaliśmy osobnych bibliotek do formatów chmur,
PCAP ani sieci. Korzystamy z System.IO, System.Net.Sockets, System.Numerics,
Task, CancellationToken i ograniczonych kolejek System.Threading.Channels.

Testy korzystają z xUnit 2.9.3, xunit.runner.visualstudio 3.1.5
i Microsoft.NET.Test.Sdk 17.14.1. Pakiety przywraca NuGet.
Pełną listę, także zależności pośrednich, pokazuje:

```powershell
dotnet list LidarViewer.slnx package --include-transitive
```

Rozwiązanie LidarViewer.slnx zawiera cztery projekty:

- Core — model punktu, dekoder VLP-16, PCAP, import i eksport, filtry, pomiar oraz odbiór i nagrywanie Live.
- Visualization — chmura, kolory, kamera, wskazywanie punktów, linia pomiaru i okręgi.
- Desktop — okno WPF, przyciski, wybór plików i sterowanie źródłami danych.
- Core.Tests — testy logiki i lokalnego odbioru UDP. Nie uruchamiają okna WPF.

Przykładowe dane są w samples, a lokalny nadajnik testowy w tools.
Odczyt, filtrowanie i przygotowanie geometrii działają w tle. Przypisanie
geometrii do widoku odbywa się na wątku WPF. Kolejka Live przechowuje tylko
najnowszą gotową chmurę, więc nie zbiera zaległych klatek.

## Co działa

- Odtwarzanie klasycznych PCAP 2.4 z Ethernet/IPv4 i pomiarami VLP-16 Strongest, Last oraz Dual.
- Pauza i wznowienie, suwak czasu, poprzednia/następna klatka, szybkości 0,5x, 1x, 2x i 4x.
- Import XYZ, PLY ASCII i binary little-endian, PCD ASCII i binary bez kompresji; XYZ i opcjonalna intensywność/RGB.
- Eksport widocznych punktów do XYZ, PLY ASCII i PCD ASCII.
- Kamera 3D, widoki XY/XZ/YZ i kolor według odległości, wysokości, intensywności, RGB albo jednolicie na niebiesko.
- Łączone filtry odległości, wysokości i intensywności.
- Wskazanie punktu, współrzędne i odległość przestrzenna od LiDAR-u, znacznik i linia pomiaru.
- Przybliżone śledzenie najbliższego punktu między klatkami, w granicy 2 m od poprzedniej pozycji.
- Okręgi w XY co 1, 5 lub 10 m, domyślnie wyłączone, z kryciem 23%.
- Symulacja domku z podłożem, odbiór Live i nagrywanie do PCAP.

## Uruchomienie

Potrzebny jest Windows, sterownik grafiki z obsługą DirectX 11 i .NET 10 SDK.
W Visual Studio 2026 zainstaluj obciążenie „Programowanie aplikacji klasycznych
dla platformy .NET”. Otwórz LidarViewer.slnx, przywróć pakiety NuGet i ustaw
LidarViewer.Desktop jako projekt startowy. Uruchom przez F5 lub Ctrl+F5.
Przy pierwszym przywracaniu pakietów potrzebny jest dostęp do NuGet.

Można też użyć PowerShell w katalogu repozytorium:

```powershell
dotnet restore LidarViewer.slnx
dotnet build LidarViewer.slnx -c Release --no-restore
dotnet test LidarViewer.slnx -c Release --no-build
dotnet run --project src/LidarViewer.Desktop -c Release --no-build
```

Po starcie otwiera się samples/demo.pcap: około 10 sekund przykładowego nagrania
VLP-16. Plik jest kopiowany przy budowaniu i publikowaniu. Nie wymaga ścieżki
konkretnego użytkownika. W samples są też domek.xyz, domek.ply i domek.pcd.

## Obsługa

„Otwórz plik” przyjmuje PCAP, XYZ, PLY i PCD. Zmiana źródła kończy poprzedni
odbiór lub odtwarzanie. W PCAP „Pauza/Start” wstrzymuje i wznawia nagranie.
Suwak oraz przyciski klatek przewijają w trybie pauzy. Nie ma osobnego Stop
odtwarzania. Czasy klatek pochodzą z PCAP, dlatego odstępy mogą się różnić.
Nagranie jest indeksowane raz podczas otwierania, a suwak odczytuje wybraną klatkę.

Prawy przycisk myszy obraca kamerę 3D, Shift + prawy przesuwa, a kółko przybliża.
W 2D dostępne są przesuwanie i zoom. „Włącz pomiar” pozwala wskazać punkt lewym
przyciskiem; wynik to sqrt(X² + Y² + Z²). Przewinięcie PCAP czyści pomiar.

W panelach filtrów wybierz kolory i zakresy punktów. Aktywne filtry łączą się
warunkiem AND. Pola przyjmują kropkę lub przecinek. Filtr intensywności pomija
punkty bez tego atrybutu. „Zrób zrzut do pliku .ply” zapisuje aktualnie widoczną
chmurę; w oknie można również wybrać XYZ lub PCD. Pomocnicze linie i okręgi
nie są eksportowane. Wyliczone kolory widoku nie zastępują RGB z pliku.

W Live wybierz lokalny IPv4 lub 0.0.0.0 i port pomiarowy, zwykle 2368.
„Połącz Live” otwiera nasłuch; nie konfiguruje czujnika. Przykładowo dla LiDAR-u
192.168.1.201/24 komputer może mieć 192.168.1.100/24, a Host/Destination w czujniku
powinien wskazywać komputer. Zezwól aplikacji na przychodzący UDP w zaporze.
Adres w aplikacji jest adresem komputera, nie LiDAR-u. Przy broadcast wybierz
0.0.0.0. „Nasłuchiwanie” oznacza otwarty port; dopiero rosnące liczniki potwierdzają
odbiór. Brak danych wymaga sprawdzenia Host IP, portu, zapory, kabla i zasilania.
VeloView uruchamiaj osobno, żeby nie zajmował tego samego portu.

Do testu bez sprzętu połącz Live na 127.0.0.1:2368 i uruchom:

```powershell
./tools/Send-LiveTest.ps1 -TargetAddress 127.0.0.1 -Port 2368
```

„Rozpocznij nagrywanie” wybiera plik PCAP, a „Zatrzymaj nagrywanie” zamyka go
bez zatrzymywania podglądu. Są liczniki czasu, pakietów i rozmiaru. Zapis nie
zależy od filtrów. Rozłączenie, zmiana źródła i wyjście z aplikacji kończą zapis.
Ładunek VLP-16 pozostaje oryginalny, nagłówki Ethernet/IP/UDP są rekonstruowane
z obliczonymi sumami kontrolnymi. Nie wymaga to Npcap. Kolejka mieści 2048 pakietów;
przepełnienie lub błąd dysku zgłasza niepełne nagranie. Zapis bez danych tworzy
poprawny nagłówek PCAP, ale nie ma w nim pomiarów do odtworzenia.

## Testy i ograniczenia

Weryfikacja z 23.11.2026: Release bez błędów i ostrzeżeń, 140/140 testów xUnit.
Testy obejmują import ASCII/binarny, eksport z ponownym odczytem, filtry,
pomiar, kolory, okręgi, dekodowanie, indeks PCAP, zegar odtwarzania i UDP.
Sprawdzają również błędne pliki, anulowanie, zajęty port, restart odbiornika,
błąd dysku, przepełnienie kolejki i zamknięcie nagrania.

Wykonano pomiary CPU i pamięci dla 10 tys., 50 tys., 100 tys., 500 tys. i miliona
punktów, import/eksport miliona punktów oraz wielokrotny odczyt PCAP i chmur.
Lokalny test Live zapisał wszystkie 7535 wysłanych pakietów, bez strat kolejki;
ponowny odczyt zachował punkty. To nie jest test fizycznej sieci Ethernet.

Nie zmierzono FPS ani czasu renderowania GPU i nie wykonano bieżącego testu
klikania w GUI: narzędzie sterowania oknem nie uruchomiło się. Do ręcznego
sprawdzenia pozostają kamera, płynność, szybkie przewijanie i zmiana źródeł,
zamknięcie podczas pracy, 30-sekundowe nagranie fizycznego VLP-16 oraz porównanie
z VeloView. Testy logiki nie zastępują tych sprawdzeń.

Nie obsługujemy PCAPNG, PLY big-endian i PCD binary_compressed. Śledzenie pomiaru
jest przybliżone i może przejść na sąsiednią powierzchnię. Dla eksportu PLY/PCD
punkty muszą mieć wspólne atrybuty; przy częściowym RGB/intensywności wybierz XYZ.
Pakietów utraconych przed odbiorem przez aplikację nie można odzyskać.
Nagłe przerwanie procesu może zostawić niekompletny plik. Płynność dużych chmur
zależy od komputera; wyniki CPU nie gwarantują określonego FPS.
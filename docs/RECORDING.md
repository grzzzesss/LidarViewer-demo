# Nagrywanie Live do PCAP

## Jak działa zapis

W panelu LiDAR LIVE kliknij „Rozpocznij nagrywanie”, wybierz plik .pcap,
a następnie „Zatrzymaj nagrywanie”. Okno zapisu pyta o zgodę na nadpisanie.
Można nagrywać również podczas nasłuchu bez danych: wynikiem jest poprawny
24-bajtowy nagłówek PCAP, ale importer zgłosi brak pomiarów.

PcapRecorder w Core zapisuje klasyczny PCAP 2.4 little-endian, Ethernet (linktype 1),
z czasem odebrania UDP w UTC i rozdzielczością mikrosekund. Przy cofnięciu zegara
systemowego czas jest ograniczany do poprzedniego znacznika, bez zmiany kolejności.
To czas dostarczenia do aplikacji, nie sprzętowy timestamp karty sieciowej.

Wybrano rekonstrukcję Ethernet + IPv4 + UDP zamiast dodatkowego przechwytywania
przez Npcap. Nagłówki NIE są oryginalnymi ramkami z przewodu. Adresy MAC są
syntetyczne, lokalnie administrowane, TTL i pozostałe pola IPv4 są odtworzone.
Adres źródłowy i port pochodzą z odbioru UDP, adres docelowy z PacketInformation
(w tym broadcast), port docelowy z lokalnego nasłuchu. Socket włącza tę opcję
przed Bind. Sumy kontrolne IPv4 i UDP są obliczane. Oryginalny ładunek 1206 bajtów
pozostaje bez zmian. Nie są zapisywane pakiety telemetryczne ani błędne pakiety.

Nie dodano bibliotek ani sterowników; jest jeden dotychczasowy odbiornik UDP.
Odbiór korzysta z ReceiveMessageFromAsync, aby ustalić rzeczywisty adres docelowy
również przy nasłuchu na 0.0.0.0. Dekoder VLP-16 i renderer nie zostały zmienione.

Każdy poprawny pakiet VLP-16 trafia do osobnej kolejki przed dekodowaniem.
Kopia bufora zabezpiecza dane przed kolejnym odbiorem. Limit to 2048 pakietów
(około 2,5 MB ładunków). Zapis działa na wątku roboczym i nie zależy od filtrów,
kamery, pomiaru ani kolejki wyświetlanych klatek. Po przepełnieniu podgląd działa
dalej, ale GUI pokazuje liczbę utraconych przez nagrywanie pakietów i ostrzeżenie.
Licznik nie obejmuje strat sieciowych i strat w buforze systemowego gniazda UDP.

Zatrzymanie zamyka kolejkę i czeka na zapis przyjętych pakietów, Flush oraz Dispose.
Rozłączenie, zmiana źródła i zamknięcie aplikacji czekają na zakończenie odbiornika,
który automatycznie kończy nagranie. Błąd dysku wyłącza zapis, ale pozostawia
podgląd Live. Zapis próbuje usunąć niedokończony rekord; po błędzie trzeba sprawdzić
plik i nie ma gwarancji odzyskania wszystkich danych. GUI nie deklaruje sukcesu
po błędzie, pokazuje zapisane, utracone i niezapisane przyjęte pakiety.

## Weryfikacja 23.11.2026

- Release: 0 błędów, 0 ostrzeżeń; 139/139 testów xUnit.
- Testy: nagłówki i długości PCAP, Ethernet/IP/UDP, adresy, porty, checksumy,
  mikrosekundy, cofnięcie czasu, kolejność i kopia danych, pusty plik, zwolnienie
  pliku, ochrona przed nadpisaniem i błędna lokalizacja, błąd dysku, ograniczenie
  kolejki i dokończenie zapisu, brak podwójnej sesji, restart nagrywania,
  automatyczne zakończenie przy rozłączeniu oraz dalsze działanie odbiornika.
- Odczyt Strongest/Last/Dual: identyczne punkty XYZ, intensywność i granice klatek;
  sprawdzono również indeksowanie i losowy odczyt przez PcapRecording.
- Rzeczywiste pakiety z próbki demo przesłane przez localhost: 7535 wysłanych,
  7535 odebranych i zapisanych, 0 odrzuconych, 0 strat kolejki nagrywania.
  Plik: 9 524 264 bajty. Po odczycie: 100 klatek, wszystkie punkty identyczne
  z oryginałem. Podgląd otrzymał 99 pełnych chmur; importer odczytuje także
  końcowy niepełny obrót.
- Niezależna kontrola capinfos i tshark (Wireshark): klasyczny PCAP, Ethernet,
  7535 pakietów, brak niepoprawnych sum IPv4/UDP i pakietów malformed.
- Nie wykonano testu GUI, fizycznego VLP-16 ani VeloView. Wireshark potwierdza
  strukturę sieciową, ale nie zastępuje testu otwarcia w VeloView.

## Test ręczny bez urządzenia

1. Uruchom Desktop, wybierz Live 127.0.0.1, port 2368 i połącz.
2. Kliknij „Rozpocznij nagrywanie” i wybierz nowy plik .pcap.
3. Uruchom z katalogu projektu:

~~~powershell
./tools/Send-LiveTest.ps1 -TargetAddress 127.0.0.1 -Port 2368
~~~

4. Sprawdź REC, rosnące liczniki i podgląd. Zmień kamerę, filtry, widok i pomiar.
   Wykonaj osobny zrzut PLY; nie powinien przerywać nagrania.
5. Zatrzymaj nagranie. Podgląd ma dalej działać; plik powinien dać się otworzyć
   przez „Otwórz plik”, z pauzą, suwakiem i szybkościami 0,5x/1x/2x/4x.
6. Uruchom nowe nagranie, a następnie sprawdź kolejno: „Rozłącz”, otwarcie pliku,
   symulację i zamknięcie aplikacji. Po każdej operacji plik ma być zamknięty.
7. Sprawdź nagranie bez danych, odmowę nadpisania, niedozwoloną lokalizację
   i ponowne nagranie po błędzie. Podgląd ma pozostać dostępny.

## Test fizycznego VLP-16

1. Podłącz i skonfiguruj czujnik według LIVE.md. Potwierdź przyrost pakietów.
2. Rozpocznij nagranie i zbieraj dane przez około 30 sekund.
3. Obróć kamerę, włącz filtry i pomiar; podgląd nie powinien się resetować.
4. Zatrzymaj nagranie bez rozłączania. Sprawdź dalszy przyrost pakietów Live.
5. Otwórz plik w LidarViewer i sprawdź geometrię, oś czasu, nawigację,
   pauzę i wszystkie szybkości. Porównaj plik w VeloView.
6. Ponów nagrywanie, rozłącz i połącz czujnik, a następnie zamknij aplikację
   podczas zapisu. Każdy zakończony plik powinien dać się ponownie otworzyć.

## Ograniczenia

Nie jest to przechwytywanie oryginalnych ramek Ethernet ani telemetryki/GPS.
Nie można odzyskać pakietów utraconych przed odbiorem przez aplikację.
Przy przeciążeniu dysku powstaje jawnie oznaczone niepełne nagranie.
Wydajność przy pełnej szybkości czujnika i zgodność z konkretną wersją VeloView
wymagają testu sprzętowego. Nagłe zakończenie procesu lub odłączenie zasilania
może pozostawić niedokończony plik; poprawne zamykanie dotyczy normalnego wyjścia.

## Źródła formatu i API

- https://github.com/the-tcpdump-group/libpcap/blob/master/pcap-savefile.manfile.in
- https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivemessagefromasync?view=net-10.0
# LiDAR LIVE — uruchomienie i weryfikacja

## Działanie

Panel „LiDAR LIVE” pozwala wybrać lokalny IPv4, port (domyślnie 2368),
uruchomić odbiór i go zatrzymać. 0.0.0.0 oznacza wszystkie lokalne interfejsy IPv4.
Pole adresu jest edytowalne, lista zawiera adresy wykryte na komputerze przy uruchomieniu.

Status „Nasłuchiwanie” oznacza otwarty lokalny port. „Odbieranie danych” pojawia się
dopiero po poprawnym pakiecie VLP-16. Po 3 sekundach bez poprawnego pakietu status
zmienia się na „Brak danych”. Liczniki rozróżniają poprawne i odrzucone datagramy.
Wyświetlany jest też nadawca ostatniego poprawnego pakietu i liczba punktów
ostatniej odebranej kompletnej chmury. Główny licznik pokazuje punkty widoczne po filtrach.

PCAP i LIVE używają Vlp16PacketDecoder: tych samych obliczeń XYZ i intensywności.
Klatka jest składana z kolejnych grup pomiarów według dotychczasowej zasady obrotu
lub limitu 100 000 punktów. Granica może wypadać wewnątrz pakietu.
Przy rozłączeniu niedokończona klatka jest odrzucana.

Odbiornik przetwarza wszystkie odebrane datagramy. Kolejka gotowych klatek ma
pojemność jednej chmury. Wolniejsze wyświetlanie zastępuje oczekującą klatkę nowszą,
aby podgląd nie narastał w pamięci ani nie odtwarzał zaległego strumienia.
System operacyjny lub sieć mogą utracić datagramy, których aplikacja nie potrafi odzyskać.

## Test bez czujnika

1. Uruchom aplikację w Visual Studio, wybierając projekt Desktop.
2. W panelu LIVE wpisz 127.0.0.1 i wolny port, np. 2368.
3. Kliknij „Połącz Live”. Aktywny PCAP lub symulacja zakończą się.
4. W PowerShell w katalogu projektu uruchom:

~~~powershell
./tools/Send-LiveTest.ps1 -TargetAddress 127.0.0.1 -Port 2368
~~~

5. Powinny rosnąć liczniki poprawnych pakietów i pojawić się chmura z 16 wiązek
   o odległości około 10 m, z intensywnością 80–200. Pełny obrót testowy zawiera
   9600 punktów przed filtrowaniem. Przed porównaniem liczników wyłącz filtry.
6. Sprawdź kamerę, widoki XY/XZ/YZ, okręgi i pomiar. Włącz filtry oraz zapisz
   klatkę przez „Zrób zrzut do pliku .ply”, następnie otwórz wynik.
7. Po końcu nadawania odczekaj 3 s. Oczekiwany status to „Brak danych”.
8. Kliknij „Rozłącz”. Ostatnia chmura pozostanie widoczna. Uruchom LIVE ponownie.
9. Podczas nadawania otwórz plik statyczny lub uruchom symulację. LIVE powinien
   zakończyć się i przestać aktualizować scenę.
10. Sprawdź błędny IPv4, port spoza zakresu i port zajęty przez inny odbiornik.
    Po błędzie powinna pozostać możliwość ponownego połączenia.

Nadajnik jest małym narzędziem testowym, wysyła syntetyczne pomiary.
Jego parametry -Packets i -Port pozwalają skrócić test i uniknąć zajętego portu.
Nie zmienia konfiguracji systemu ani urządzenia.

## Fizyczny VLP-16 na Windows

1. Zasil czujnik i podłącz go do interfejsu zgodnie z instrukcją producenta.
2. Połącz interfejs czujnika z kartą Ethernet komputera.
3. Sprawdź rzeczywisty adres czujnika, maskę i adres karty komputera.
   Muszą umożliwiać komunikację w tej samej podsieci i mieć różne adresy.
   Przykład wyłącznie dla czujnika 192.168.1.201/24: komputer 192.168.1.100/24.
4. W konfiguracji czujnika sprawdź Host/Destination IP oraz Data Port.
   Docelowy adres powinien odpowiadać komputerowi lub odpowiedniemu broadcastowi.
   Typowy port pomiarowy to 2368. Aplikacja nie ustawia tych parametrów zdalnie.
5. W LidarViewer wybierz lokalny adres karty Ethernet lub 0.0.0.0 i zgodny port.
6. Kliknij „Połącz Live”. Sprawdź status oraz wzrost liczby poprawnych pakietów.
   Brak danych oznacza konieczność sprawdzenia adresu docelowego, kabla, zasilania,
   konfiguracji interfejsu i reguł zapory Windows dla przychodzącego UDP.
   Samo otwarcie portu nie potwierdza obecności czujnika.
7. Porównaj geometrię z VeloView dla tego samego urządzenia i ustawień.
   Jeżeli aplikacje rywalizują o ten sam port, uruchamiaj je kolejno.
8. Sprawdź filtry, zmianę widoku, kamerę, pomiar i eksport podczas odbioru.
9. Rozłącz, połącz ponownie, a następnie zamknij aplikację podczas nasłuchu.
   Sprawdź ponowne uruchomienie i dostępność portu.

Zwykły odbiór UDP nie wymaga uruchamiania LidarViewer jako administrator.
Decyzje zapory oraz ustawienia interfejsu sieciowego należą do użytkownika.

Instrukcja producenta:
https://data.ouster.io/downloads/velodyne/user-manual/vlp-16-user-manual-revf.pdf

## Weryfikacja 23.11.2026

Ponownie przeanalizowano odbiornik, dekoder i przełączanie źródeł w Desktop.
Kompilacja Release w projekcie głównym i demo: 0 błędów i 0 ostrzeżeń.
W obu kopiach przeszło 129/129 testów xUnit. Dodatkowo sprawdzono nasłuch
na 0.0.0.0, tryb Last, odrzucanie innego modelu czujnika oraz powrót do odbioru
po stanie „Brak danych”. Dotychczasowe testy obejmują także Strongest i Dual,
restart, zajęty port, anulowanie oraz kolejkę najnowszej chmury.

Dodatkowy test rzeczywistych pakietów z samples/demo.pcap (kopii demo):
7535 datagramów wysłanych przez UDP na localhost, 7535 poprawnie odebranych,
0 odrzuconych, 99 kompletnych chmur. Ostatnia chmura: 23 161 punktów.
Nadajnik używał kontrolowanego tempa; test nie potwierdza wydajności Ethernetu
przy pełnej prędkości sprzętu.

Nie przeprowadzono testu na fizycznym czujniku ani bieżącego testu GUI.
Testy localhost nie potwierdzają konfiguracji Ethernet ani zapory.

## Najważniejsze przed podłączeniem sprzętu

„Połącz Live” otwiera lokalny port. Nie wyszukuje czujnika i nie zmienia jego
konfiguracji. Do pola lokalnego IPv4 nie wpisuj adresu LiDAR-u.
Przy pierwszym uruchomieniu wybierz 0.0.0.0, szczególnie gdy czujnik wysyła
broadcast. Konkretny lokalny adres jest odpowiedni przy transmisji unicast.

Przykład dla czujnika 192.168.1.201 z maską 255.255.255.0:
- karta Ethernet komputera: 192.168.1.100, maska 255.255.255.0;
- w przeglądarce otwórz http://192.168.1.201;
- Host (Destination) IP czujnika: 192.168.1.100, Data Port: 2368;
- w aplikacji: lokalny adres 0.0.0.0, port 2368;
- zezwól aplikacji na przychodzący UDP w zaporze dla używanego profilu sieci;
- zamknij VeloView i inne programy korzystające z tego samego portu;
- upewnij się, że laser i silnik są włączone, a FOV obejmuje pełny obrót.

Adresy są przykładem: używane urządzenie może mieć zmienioną konfigurację.
Sprawdź ustawienia przed ich zmianą. Nie ustawiaj obu urządzeniom tego samego IP.
Po podłączeniu muszą rosnąć liczniki poprawnych pakietów i punktów oraz pojawić
się chmura. Sam status „Nasłuchiwanie” potwierdza tylko otwarcie portu.
Gdy rosną wyłącznie odrzucone pakiety, sprawdź model czujnika i Data Port
(port telemetryczny 8308 nie dostarcza chmury). Gdy oba liczniki stoją,
sprawdź Host IP, adres Ethernet, zaporę, kabel oraz zasilanie.

## Ograniczenia

Odbierany jest format pomiarowy VLP-16 1206 bajtów, single/last/dual return,
zgodny z istniejącym dekoderem i jego obecną kalibracją. Obsługiwane są lokalne
interfejsy IPv4. W danej sesji kieruj strumień z jednego czujnika na wybrany port.
Nie dodano filtrowania po IP nadawcy, FPS, nagrywania ani zdalnej konfiguracji.
Lista lokalnych interfejsów jest pobierana przy uruchomieniu, adres można wpisać ręcznie.

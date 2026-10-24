namespace RookHub.Api.Tests.TournamentDirectory;

/// <summary>Die aufgenommenen Erwartungen zu <see cref="ExternalSweepGoldenTests"/>, je Quelle.</summary>
internal static class ExternalSweepGolden
{
    internal static readonly Dictionary<string, string> Expected = new()
    {
        ["Frsah"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 1, Matched = 0, Retired = 0 }
            Log Information: FRSah-Kalender: 13 gelesen, 12 neu, 1 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: FRSah: ro1001 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: FRSah-Kalender: 13 gelesen, 0 neu, 0 verortet, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FRSah-Kalender: 11 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FRSah-Kalender: 11 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FRSah-Kalender: 1 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung RomanianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 1, Matched = 1, Retired = 0 }
            Log Warning: FRSah: Zwischenstand konnte nicht gespeichert werden
            Log Information: FRSah-Kalender: 2 gelesen, 1 neu, 1 verortet, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung RomanianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: FRSah: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk RomanianChessFederation 1001 url=https://frsah.ro/event/1001/ erst=ja zuletzt=ja
            Eintrag ro1001 cr=- name=Danubius Kronstadt Memorial fed=ROU state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag ro1002 cr=- name=Cup Beta Rapid fed=ROU state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 1002 url=https://frsah.ro/event/1002/ erst=ja zuletzt=ja
            Eintrag ro1003 cr=- name=Verschwindibus Pokal fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk RomanianChessFederation 1003 url=https://frsah.ro/event/1003/ erst=ja zuletzt=ja
            Eintrag ro2001 cr=- name=Serienturnier Nummer1 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2001 url=https://frsah.ro/event/2001/ erst=ja zuletzt=ja
            Eintrag ro2002 cr=- name=Serienturnier Nummer2 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2002 url=https://frsah.ro/event/2002/ erst=ja zuletzt=ja
            Eintrag ro2003 cr=- name=Serienturnier Nummer3 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2003 url=https://frsah.ro/event/2003/ erst=ja zuletzt=ja
            Eintrag ro2004 cr=- name=Serienturnier Nummer4 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2004 url=https://frsah.ro/event/2004/ erst=ja zuletzt=ja
            Eintrag ro2005 cr=- name=Serienturnier Nummer5 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2005 url=https://frsah.ro/event/2005/ erst=ja zuletzt=ja
            Eintrag ro2006 cr=- name=Serienturnier Nummer6 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2006 url=https://frsah.ro/event/2006/ erst=ja zuletzt=ja
            Eintrag ro2007 cr=- name=Serienturnier Nummer7 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2007 url=https://frsah.ro/event/2007/ erst=ja zuletzt=ja
            Eintrag ro2008 cr=- name=Serienturnier Nummer8 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2008 url=https://frsah.ro/event/2008/ erst=ja zuletzt=ja
            Eintrag ro2009 cr=- name=Serienturnier Nummer9 fed=ROU state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk RomanianChessFederation 2009 url=https://frsah.ro/event/2009/ erst=ja zuletzt=ja
            
            """,
        ["Wcu"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: WCU-Saisonkalender: 13 gelesen, 12 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: wcu: wlfe675fe7aaee geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: WCU-Saisonkalender: 13 gelesen, 0 neu, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: WCU-Saisonkalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: WCU-Saisonkalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: WCU-Saisonkalender: 1 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung WelshChessUnion uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: wcu: Zwischenstand konnte nicht gespeichert werden
            Log Information: WCU-Saisonkalender: 2 gelesen, 1 neu, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung WelshChessUnion uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: wcu: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk WelshChessUnion wlfe675fe7aaee url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl483029d52621 cr=- name=Serienturnier Nummer4 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl483029d52621 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6c94e35ccc35 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl6f6a4e56098c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl77459b9b941b cr=- name=Serienturnier Nummer3 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl77459b9b941b url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl85d6385b945c cr=- name=Serienturnier Nummer1 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wl85d6385b945c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wl8c9a013ab70c cr=- name=Verschwindibus Pokal fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk WelshChessUnion wl8c9a013ab70c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wla20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wla20a2b7bb084 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlb281bc2c616c cr=- name=Cup Beta Rapid fed=WLS state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlb281bc2c616c url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wle5e53c784d5d cr=- name=Serienturnier Nummer8 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wle5e53c784d5d url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf1cfa5ebb149 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=WLS state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk WelshChessUnion wlf37f3f2b0dc5 url=https://www.welshchessunion.uk/calendar/ erst=ja zuletzt=ja
            Eintrag wlfe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=WLS state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            
            """,
        ["Cfc"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: CFC-Kalender: 13 gelesen, 12 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: cfc: cafe675fe7aaee geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: CFC-Kalender: 13 gelesen, 0 neu, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: CFC-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: CFC-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: CFC-Kalender: 1 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung CanadianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: cfc: Zwischenstand konnte nicht gespeichert werden
            Log Information: CFC-Kalender: 2 gelesen, 1 neu, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung CanadianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: cfc: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=CAN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk CanadianChessFederation cafe675fe7aaee url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca483029d52621 cr=- name=Serienturnier Nummer4 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca483029d52621 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6c94e35ccc35 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca6f6a4e56098c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca77459b9b941b cr=- name=Serienturnier Nummer3 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca77459b9b941b url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca85d6385b945c cr=- name=Serienturnier Nummer1 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation ca85d6385b945c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag ca8c9a013ab70c cr=- name=Verschwindibus Pokal fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk CanadianChessFederation ca8c9a013ab70c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caa20a2b7bb084 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cab281bc2c616c cr=- name=Cup Beta Rapid fed=CAN state=ON start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cab281bc2c616c url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cae5e53c784d5d cr=- name=Serienturnier Nummer8 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation cae5e53c784d5d url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf1cfa5ebb149 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag caf37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=CAN state=ON start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk CanadianChessFederation caf37f3f2b0dc5 url=https://www.chess.ca/en/events/ erst=ja zuletzt=ja
            Eintrag cafe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=CAN state=ON start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            
            """,
        ["Szs"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: SZS-Kalender: 13 gelesen, 12 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: SZS: sl1001 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: SZS-Kalender: 13 gelesen, 0 neu, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: SZS-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: SZS-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: SZS-Kalender: 1 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung SlovenianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: SZS: Zwischenstand konnte nicht gespeichert werden
            Log Information: SZS-Kalender: 2 gelesen, 1 neu, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung SlovenianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: ExternalSweepResult { Read = 30, Added = 30, Updated = 0, Matched = 0, Retired = 0 }
            Log Warning: SZS: Zwischenstand konnte nicht gespeichert werden
            Log Information: SZS-Kalender: 30 gelesen, 30 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung SlovenianChessFederation uebersprungen: der Lauf lieferte 30 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk SlovenianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag sl1001 cr=- name=Danubius Kronstadt Memorial fed=SLO state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sl1002 cr=- name=Cup Beta Rapid fed=SLO state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag sl1003 cr=- name=Verschwindibus Pokal fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk SlovenianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag sl2001 cr=- name=Serienturnier Nummer1 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag sl2002 cr=- name=Serienturnier Nummer2 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag sl2003 cr=- name=Serienturnier Nummer3 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag sl2004 cr=- name=Serienturnier Nummer4 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag sl2005 cr=- name=Serienturnier Nummer5 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag sl2006 cr=- name=Serienturnier Nummer6 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag sl2007 cr=- name=Serienturnier Nummer7 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag sl2008 cr=- name=Serienturnier Nummer8 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag sl2009 cr=- name=Serienturnier Nummer9 fed=SLO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk SlovenianChessFederation 2009 url=- erst=ja zuletzt=ja
            
            """,
        ["Ecf"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 1, Matched = 0, Retired = 0 }
            Log Information: ECF-Kalender: 13 gelesen, 12 neu, 1 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: ECF: en1001 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: ECF-Kalender: 13 gelesen, 0 neu, 0 verortet, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: ECF-Kalender: 11 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: ECF-Kalender: 11 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: ECF-Kalender: 1 gelesen, 0 neu, 0 verortet, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung EnglishChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 1, Matched = 1, Retired = 0 }
            Log Warning: ECF: Zwischenstand konnte nicht gespeichert werden
            Log Information: ECF-Kalender: 2 gelesen, 1 neu, 1 verortet, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung EnglishChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: ECF: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk EnglishChessFederation 1001 url=https://www.englishchess.org.uk/event/1001/ erst=ja zuletzt=ja
            Eintrag en1001 cr=- name=Danubius Kronstadt Memorial fed=ENG state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag en1002 cr=- name=Cup Beta Rapid fed=ENG state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 1002 url=https://www.englishchess.org.uk/event/1002/ erst=ja zuletzt=ja
            Eintrag en1003 cr=- name=Verschwindibus Pokal fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk EnglishChessFederation 1003 url=https://www.englishchess.org.uk/event/1003/ erst=ja zuletzt=ja
            Eintrag en2001 cr=- name=Serienturnier Nummer1 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2001 url=https://www.englishchess.org.uk/event/2001/ erst=ja zuletzt=ja
            Eintrag en2002 cr=- name=Serienturnier Nummer2 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2002 url=https://www.englishchess.org.uk/event/2002/ erst=ja zuletzt=ja
            Eintrag en2003 cr=- name=Serienturnier Nummer3 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2003 url=https://www.englishchess.org.uk/event/2003/ erst=ja zuletzt=ja
            Eintrag en2004 cr=- name=Serienturnier Nummer4 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2004 url=https://www.englishchess.org.uk/event/2004/ erst=ja zuletzt=ja
            Eintrag en2005 cr=- name=Serienturnier Nummer5 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2005 url=https://www.englishchess.org.uk/event/2005/ erst=ja zuletzt=ja
            Eintrag en2006 cr=- name=Serienturnier Nummer6 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2006 url=https://www.englishchess.org.uk/event/2006/ erst=ja zuletzt=ja
            Eintrag en2007 cr=- name=Serienturnier Nummer7 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2007 url=https://www.englishchess.org.uk/event/2007/ erst=ja zuletzt=ja
            Eintrag en2008 cr=- name=Serienturnier Nummer8 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2008 url=https://www.englishchess.org.uk/event/2008/ erst=ja zuletzt=ja
            Eintrag en2009 cr=- name=Serienturnier Nummer9 fed=ENG state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk EnglishChessFederation 2009 url=https://www.englishchess.org.uk/event/2009/ erst=ja zuletzt=ja
            
            """,
        ["ChessHu"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: chess.hu-Kalender: 13 gelesen, 12 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: chess.hu: hu1001 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: chess.hu-Kalender: 13 gelesen, 0 neu, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: chess.hu-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: chess.hu-Kalender: 11 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: chess.hu-Kalender: 1 gelesen, 0 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung HungarianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: chess.hu: Zwischenstand konnte nicht gespeichert werden
            Log Information: chess.hu-Kalender: 2 gelesen, 1 neu, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung HungarianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: ExternalSweepResult { Read = 30, Added = 30, Updated = 0, Matched = 0, Retired = 0 }
            Log Warning: chess.hu: Zwischenstand konnte nicht gespeichert werden
            Log Information: chess.hu-Kalender: 30 gelesen, 30 neu, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung HungarianChessFederation uebersprungen: der Lauf lieferte 30 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk HungarianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag hu1001 cr=- name=Danubius Kronstadt Memorial fed=HUN state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag hu1002 cr=- name=Cup Beta Rapid fed=HUN state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag hu1003 cr=- name=Verschwindibus Pokal fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk HungarianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag hu2001 cr=- name=Serienturnier Nummer1 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag hu2002 cr=- name=Serienturnier Nummer2 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag hu2003 cr=- name=Serienturnier Nummer3 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag hu2004 cr=- name=Serienturnier Nummer4 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag hu2005 cr=- name=Serienturnier Nummer5 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag hu2006 cr=- name=Serienturnier Nummer6 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag hu2007 cr=- name=Serienturnier Nummer7 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag hu2008 cr=- name=Serienturnier Nummer8 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag hu2009 cr=- name=Serienturnier Nummer9 fed=HUN state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk HungarianChessFederation 2009 url=- erst=ja zuletzt=ja
            
            """,
        ["Knsb"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 12, Added = 12, Updated = 12, Matched = 0, Retired = 0 }
            Log Information: KNSB-Kalender: 12 gelesen, 12 neu, 12 mit neu bekannter Bedenkzeit, 0 zugeordnet, 0 zurueckgezogen
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 12, Added = 0, Updated = 0, Matched = 1, Retired = 1 }
            Log Information: KNSB: nlc52806197c2d geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: KNSB-Kalender: 12 gelesen, 0 neu, 0 mit neu bekannter Bedenkzeit, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 10, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: KNSB-Kalender: 10 gelesen, 0 neu, 0 mit neu bekannter Bedenkzeit, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 10, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: KNSB-Kalender: 10 gelesen, 0 neu, 0 mit neu bekannter Bedenkzeit, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: KNSB-Kalender: 1 gelesen, 0 neu, 0 mit neu bekannter Bedenkzeit, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung DutchChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 1, Matched = 1, Retired = 0 }
            Log Warning: KNSB: Zwischenstand konnte nicht gespeichert werden
            Log Information: KNSB-Kalender: 2 gelesen, 1 neu, 1 mit neu bekannter Bedenkzeit, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung DutchChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: KNSB: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk DutchChessFederation c52806197c2d url=https://schaakbond.nl/event/toernooi-1001/ erst=ja zuletzt=ja
            Eintrag nl1f53671d33a7 cr=- name=Serienturnier Nummer1 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 1f53671d33a7 url=https://schaakbond.nl/event/toernooi-2001/ erst=ja zuletzt=ja
            Eintrag nl35d5cdace202 cr=- name=Serienturnier Nummer5 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 35d5cdace202 url=https://schaakbond.nl/event/toernooi-2005/ erst=ja zuletzt=ja
            Eintrag nl514ae2538e52 cr=- name=Cup Beta Rapid fed=NED state=- start=S+7 ende=S+7 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 514ae2538e52 url=https://schaakbond.nl/event/toernooi-1002/ erst=ja zuletzt=ja
            Eintrag nl748034dace19 cr=- name=Verschwindibus Pokal fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk DutchChessFederation 748034dace19 url=https://schaakbond.nl/event/toernooi-1003/ erst=ja zuletzt=ja
            Eintrag nl76526a957db0 cr=- name=Serienturnier Nummer7 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 76526a957db0 url=https://schaakbond.nl/event/toernooi-2007/ erst=ja zuletzt=ja
            Eintrag nl81bdef797f9c cr=- name=Serienturnier Nummer4 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 81bdef797f9c url=https://schaakbond.nl/event/toernooi-2004/ erst=ja zuletzt=ja
            Eintrag nl8d68a0f08fe3 cr=- name=Serienturnier Nummer6 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 8d68a0f08fe3 url=https://schaakbond.nl/event/toernooi-2006/ erst=ja zuletzt=ja
            Eintrag nl9905514496e5 cr=- name=Serienturnier Nummer9 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation 9905514496e5 url=https://schaakbond.nl/event/toernooi-2009/ erst=ja zuletzt=ja
            Eintrag nlbbb998e3b01d cr=- name=Serienturnier Nummer8 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation bbb998e3b01d url=https://schaakbond.nl/event/toernooi-2008/ erst=ja zuletzt=ja
            Eintrag nlbe227e5cbd7a cr=- name=Serienturnier Nummer3 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation be227e5cbd7a url=https://schaakbond.nl/event/toernooi-2003/ erst=ja zuletzt=ja
            Eintrag nlc52806197c2d cr=- name=Danubius Kronstadt Memorial fed=NED state=- start=S+0 ende=S+0 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag nlea3da42d0a32 cr=- name=Serienturnier Nummer2 fed=NED state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk DutchChessFederation ea3da42d0a32 url=https://schaakbond.nl/event/toernooi-2002/ erst=ja zuletzt=ja
            
            """,
        ["Fsi"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FSI-Kalender: 13 gelesen, 12 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 1, Matched = 1, Retired = 1 }
            Log Information: FSI: it1001 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: FSI-Kalender: 13 gelesen, 0 neu, 1 ergaenzt, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FSI-Kalender: 11 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FSI-Kalender: 11 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: FSI-Kalender: 1 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung ItalianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: FSI: Zwischenstand konnte nicht gespeichert werden
            Log Information: FSI-Kalender: 2 gelesen, 1 neu, 0 ergaenzt, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung ItalianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: ExternalSweepResult { Read = 30, Added = 30, Updated = 0, Matched = 0, Retired = 0 }
            Log Warning: FSI: Zwischenstand konnte nicht gespeichert werden
            Log Information: FSI-Kalender: 30 gelesen, 30 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung ItalianChessFederation uebersprungen: der Lauf lieferte 30 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=ITA state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ItalianChessFederation 1001 url=- erst=ja zuletzt=ja
            Eintrag it1001 cr=- name=Danubius Kronstadt Memorial fed=ITA state=Lazio start=S+0 ende=S+0 we=True ort=Nirgendwo, Roma geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag it1002 cr=- name=Cup Beta Rapid fed=ITA state=Lazio start=S+7 ende=S+7 we=True ort=Testhausen, Roma geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 1002 url=- erst=ja zuletzt=ja
            Eintrag it1003 cr=- name=Verschwindibus Pokal fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ItalianChessFederation 1003 url=- erst=ja zuletzt=ja
            Eintrag it2001 cr=- name=Serienturnier Nummer1 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2001 url=- erst=ja zuletzt=ja
            Eintrag it2002 cr=- name=Serienturnier Nummer2 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2002 url=- erst=ja zuletzt=ja
            Eintrag it2003 cr=- name=Serienturnier Nummer3 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag it2004 cr=- name=Serienturnier Nummer4 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag it2005 cr=- name=Serienturnier Nummer5 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag it2006 cr=- name=Serienturnier Nummer6 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag it2007 cr=- name=Serienturnier Nummer7 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag it2008 cr=- name=Serienturnier Nummer8 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag it2009 cr=- name=Serienturnier Nummer9 fed=ITA state=Lazio start=S+1 ende=S+1 we=True ort=Roma geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ItalianChessFederation 2009 url=- erst=ja zuletzt=ja
            
            """,
        ["Schachbund"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: schachbund-Turnierdatenbank: 13 gelesen, 12 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 1, Matched = 1, Retired = 1 }
            Log Information: schachbund: defe675fe7aaee geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: schachbund-Turnierdatenbank: 13 gelesen, 0 neu, 1 ergaenzt, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: schachbund-Turnierdatenbank: 11 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: schachbund-Turnierdatenbank: 11 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: schachbund-Turnierdatenbank: 1 gelesen, 0 neu, 0 ergaenzt, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung GermanChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 0, Matched = 1, Retired = 0 }
            Log Warning: schachbund: Zwischenstand konnte nicht gespeichert werden
            Log Information: schachbund-Turnierdatenbank: 2 gelesen, 1 neu, 0 ergaenzt, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung GermanChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: schachbund: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk GermanChessFederation defe675fe7aaee url=https://www.schachbund.de/turnierdetails/1001.html erst=ja zuletzt=ja
            Eintrag de483029d52621 cr=- name=Serienturnier Nummer4 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de483029d52621 url=https://www.schachbund.de/turnierdetails/2004.html erst=ja zuletzt=ja
            Eintrag de6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6c94e35ccc35 url=https://www.schachbund.de/turnierdetails/2002.html erst=ja zuletzt=ja
            Eintrag de6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de6f6a4e56098c url=https://www.schachbund.de/turnierdetails/2006.html erst=ja zuletzt=ja
            Eintrag de77459b9b941b cr=- name=Serienturnier Nummer3 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de77459b9b941b url=https://www.schachbund.de/turnierdetails/2003.html erst=ja zuletzt=ja
            Eintrag de85d6385b945c cr=- name=Serienturnier Nummer1 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation de85d6385b945c url=https://www.schachbund.de/turnierdetails/2001.html erst=ja zuletzt=ja
            Eintrag de8c9a013ab70c cr=- name=Verschwindibus Pokal fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk GermanChessFederation de8c9a013ab70c url=https://www.schachbund.de/turnierdetails/1003.html erst=ja zuletzt=ja
            Eintrag dea20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dea20a2b7bb084 url=https://www.schachbund.de/turnierdetails/2005.html erst=ja zuletzt=ja
            Eintrag deb281bc2c616c cr=- name=Cup Beta Rapid fed=GER state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation deb281bc2c616c url=https://www.schachbund.de/turnierdetails/1002.html erst=ja zuletzt=ja
            Eintrag dee5e53c784d5d cr=- name=Serienturnier Nummer8 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation dee5e53c784d5d url=https://www.schachbund.de/turnierdetails/2008.html erst=ja zuletzt=ja
            Eintrag def1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def1cfa5ebb149 url=https://www.schachbund.de/turnierdetails/2007.html erst=ja zuletzt=ja
            Eintrag def37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=GER state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk GermanChessFederation def37f3f2b0dc5 url=https://www.schachbund.de/turnierdetails/2009.html erst=ja zuletzt=ja
            Eintrag defe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=GER state=- start=S+0 ende=S+0 we=True ort=Nirgendwo geo=None lat=- platz=- gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            
            """,
        ["Sjakk"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 5, Matched = 0, Retired = 0 }
            Log Information: sjakk.no-Aktivitaeten: 13 gelesen, 12 neu, 5 mit Detailseite, 0 zugeordnet, 0 zurueckgezogen
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=- erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=- erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=- erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=- erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=- erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=- erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 5, Matched = 1, Retired = 1 }
            Log Information: sjakk.no: nofe675fe7aaee geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: sjakk.no-Aktivitaeten: 13 gelesen, 0 neu, 5 mit Detailseite, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=- erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=- erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 2, Matched = 0, Retired = 0 }
            Log Information: sjakk.no-Aktivitaeten: 11 gelesen, 0 neu, 2 mit Detailseite, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: sjakk.no-Aktivitaeten: 11 gelesen, 0 neu, 0 mit Detailseite, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: sjakk.no-Aktivitaeten: 1 gelesen, 0 neu, 0 mit Detailseite, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung NorwegianChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 1, Matched = 1, Retired = 0 }
            Log Warning: sjakk.no: Zwischenstand konnte nicht gespeichert werden
            Log Information: sjakk.no-Aktivitaeten: 2 gelesen, 1 neu, 1 mit Detailseite, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung NorwegianChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: sjakk.no: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk NorwegianChessFederation 1001 url=https://www.sjakk.no/aktiviteter/1001 erst=ja zuletzt=ja
            Eintrag no483029d52621 cr=- name=Serienturnier Nummer4 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2004 url=https://www.sjakk.no/aktiviteter/2004 erst=ja zuletzt=ja
            Eintrag no6c94e35ccc35 cr=- name=Serienturnier Nummer2 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2002 url=https://www.sjakk.no/aktiviteter/2002 erst=ja zuletzt=ja
            Eintrag no6f6a4e56098c cr=- name=Serienturnier Nummer6 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2006 url=https://www.sjakk.no/aktiviteter/2006 erst=ja zuletzt=ja
            Eintrag no77459b9b941b cr=- name=Serienturnier Nummer3 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2003 url=https://www.sjakk.no/aktiviteter/2003 erst=ja zuletzt=ja
            Eintrag no85d6385b945c cr=- name=Serienturnier Nummer1 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2001 url=https://www.sjakk.no/aktiviteter/2001 erst=ja zuletzt=ja
            Eintrag no8c9a013ab70c cr=- name=Verschwindibus Pokal fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1003 url=https://www.sjakk.no/aktiviteter/1003 erst=ja zuletzt=ja
            Eintrag noa20a2b7bb084 cr=- name=Serienturnier Nummer5 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2005 url=https://www.sjakk.no/aktiviteter/2005 erst=ja zuletzt=ja
            Eintrag nob281bc2c616c cr=- name=Cup Beta Rapid fed=NOR state=- start=S+7 ende=S+7 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 1002 url=https://www.sjakk.no/aktiviteter/1002 erst=ja zuletzt=ja
            Eintrag noe5e53c784d5d cr=- name=Serienturnier Nummer8 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2008 url=https://www.sjakk.no/aktiviteter/2008 erst=ja zuletzt=ja
            Eintrag nof1cfa5ebb149 cr=- name=Serienturnier Nummer7 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2007 url=https://www.sjakk.no/aktiviteter/2007 erst=ja zuletzt=ja
            Eintrag nof37f3f2b0dc5 cr=- name=Serienturnier Nummer9 fed=NOR state=- start=S+1 ende=S+1 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk NorwegianChessFederation 2009 url=https://www.sjakk.no/aktiviteter/2009 erst=ja zuletzt=ja
            Eintrag nofe675fe7aaee cr=- name=Danubius Kronstadt Memorial fed=NOR state=- start=S+0 ende=S+0 we=True ort=Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            
            """,
        ["ChessScotland"] = """
            == 1 Erstlauf
            Ergebnis: ExternalSweepResult { Read = 13, Added = 12, Updated = 5, Matched = 0, Retired = 0 }
            Log Information: Chess-Scotland-Kalender: 13 gelesen, 12 neu, 5 mit Spielort, 0 zugeordnet, 0 zurueckgezogen
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=- erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=- erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=- erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=- erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=- erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=- erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=- erst=ja zuletzt=ja
            == 2 chess-results fuehrt A
            Ergebnis: ExternalSweepResult { Read = 13, Added = 0, Updated = 5, Matched = 1, Retired = 1 }
            Log Information: Chess Scotland: sc42c58c843294 geht in 9000001 auf (Danubius Kronstadt Memorial)
            Log Information: Chess-Scotland-Kalender: 13 gelesen, 0 neu, 5 mit Spielort, 1 zugeordnet, 1 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=- erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=- erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 3 C fehlt
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 2, Matched = 0, Retired = 0 }
            Log Information: Chess-Scotland-Kalender: 11 gelesen, 0 neu, 2 mit Spielort, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 4 C fehlt wieder (Karenz)
            Ergebnis: ExternalSweepResult { Read = 11, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: Chess-Scotland-Kalender: 11 gelesen, 0 neu, 0 mit Spielort, 0 zugeordnet, 0 zurueckgezogen
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 5 nur B (Bremse)
            Ergebnis: ExternalSweepResult { Read = 1, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Log Information: Chess-Scotland-Kalender: 1 gelesen, 0 neu, 0 mit Spielort, 0 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung ScottishChessFederation uebersprungen: nur 1 von 11 eigenen Eintraegen wiedergesehen (9 %, verlangt sind 90 %) — der Lauf ist unvollstaendig. Bleibt das so, zieht diese Quelle NIE etwas zurueck
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 6 leer
            Ergebnis: ExternalSweepResult { Read = 0, Added = 0, Updated = 0, Matched = 0, Retired = 0 }
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 7 Speichern scheitert
            Ergebnis: ExternalSweepResult { Read = 2, Added = 1, Updated = 1, Matched = 1, Retired = 0 }
            Log Warning: Chess Scotland: Zwischenstand konnte nicht gespeichert werden
            Log Information: Chess-Scotland-Kalender: 2 gelesen, 1 neu, 1 mit Spielort, 1 zugeordnet, 0 zurueckgezogen
            Log Warning: Verschwunden-Erkennung ScottishChessFederation uebersprungen: der Lauf lieferte 2 Kennungen, aber KEINE der 11 eigenen Eintraege — das ist ein kaputter Lauf oder ein Schluesselwechsel der Quelle, keine Massenabsage
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            == 8 Speichern scheitert mitten im Lauf
            Ergebnis: WIRFT DbUpdateException: Duplicate entry 'poison' for key 'PublicId'
            Log Warning: Chess Scotland: Zwischenstand konnte nicht gespeichert werden
            Eintrag 9000001 cr=9000001 name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=False ort=- geo=None lat=- platz=- gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=-
              Vermerk ScottishChessFederation sc42c58c843294 url=https://www.chessscotland.com/calendar/slug-1001 erst=ja zuletzt=ja
            Eintrag sc2b03ef8e3803 cr=- name=Cup Beta Rapid fed=SCO state=- start=S+7 ende=S+7 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc2b03ef8e3803 url=https://www.chessscotland.com/calendar/slug-1002 erst=ja zuletzt=ja
            Eintrag sc3174baf6ef26 cr=- name=Serienturnier Nummer2 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc3174baf6ef26 url=https://www.chessscotland.com/calendar/slug-2002 erst=ja zuletzt=ja
            Eintrag sc40d9dbb3ce50 cr=- name=Serienturnier Nummer6 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc40d9dbb3ce50 url=https://www.chessscotland.com/calendar/slug-2006 erst=ja zuletzt=ja
            Eintrag sc42c58c843294 cr=- name=Danubius Kronstadt Memorial fed=SCO state=- start=S+0 ende=S+0 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=ja fehl=0 fehlAm=- alter=None gruppe=ja
            Eintrag sc504be4ea82e6 cr=- name=Serienturnier Nummer7 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc504be4ea82e6 url=https://www.chessscotland.com/calendar/slug-2007 erst=ja zuletzt=ja
            Eintrag sc531f157c995f cr=- name=Serienturnier Nummer4 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc531f157c995f url=https://www.chessscotland.com/calendar/slug-2004 erst=ja zuletzt=ja
            Eintrag sc54264c71ef51 cr=- name=Verschwindibus Pokal fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=1 fehlAm=ja alter=None gruppe=ja
              Vermerk ScottishChessFederation sc54264c71ef51 url=https://www.chessscotland.com/calendar/slug-1003 erst=ja zuletzt=ja
            Eintrag sc73c2dbcb605f cr=- name=Serienturnier Nummer9 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc73c2dbcb605f url=https://www.chessscotland.com/calendar/slug-2009 erst=ja zuletzt=ja
            Eintrag sc7bf70e0a8c01 cr=- name=Serienturnier Nummer1 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc7bf70e0a8c01 url=https://www.chessscotland.com/calendar/slug-2001 erst=ja zuletzt=ja
            Eintrag sc9d0f3a82decc cr=- name=Serienturnier Nummer3 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation sc9d0f3a82decc url=https://www.chessscotland.com/calendar/slug-2003 erst=ja zuletzt=ja
            Eintrag scaef6113cb9ab cr=- name=Serienturnier Nummer8 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scaef6113cb9ab url=https://www.chessscotland.com/calendar/slug-2008 erst=ja zuletzt=ja
            Eintrag scc804b4cdc621 cr=- name=Serienturnier Nummer5 fed=SCO state=- start=S+1 ende=S+1 we=True ort=Community Hall, Testhausen geo=City lat=10.5 platz=Testhausen gesehen=ja weg=- fehl=0 fehlAm=- alter=None gruppe=ja
              Vermerk ScottishChessFederation scc804b4cdc621 url=https://www.chessscotland.com/calendar/slug-2005 erst=ja zuletzt=ja
            
            """,
    };
}

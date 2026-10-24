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
    };
}

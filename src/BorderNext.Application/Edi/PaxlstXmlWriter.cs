using System.Text;
using System.Xml;

namespace BorderNext.Application.Edi;

/// <summary>
/// Sortie XML d'un message PAXLST (héritage : XmlWriter du parseur EDIFACT —
/// éléments ReportingParty / Flight / Travellers et champs normalisés
/// LastName, FirstName, MiddleName, Nationality, Sex, DateOfBirth, DocType,
/// PassportNumber, DateOfExpiry, CountryOfIssue).
/// </summary>
public static class PaxlstXmlWriter
{
    /// <summary>Formats de date de la sortie XML héritée.</summary>
    public const string DateFormat = "dd.MM.yyyy";

    /// <summary>Écrit un message PAXLST au format XML.</summary>
    public static string ToXml(PaxlstMessage message)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
        }))
        {
            Write(writer, message);
        }

        return builder.ToString();
    }

    /// <summary>Écrit plusieurs messages PAXLST dans un même document XML.</summary>
    public static string ToXml(IEnumerable<PaxlstMessage> messages)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
        }))
        {
            writer.WriteStartElement("Messages");
            foreach (var message in messages)
            {
                writer.WriteStartElement("Message");
                WriteBody(writer, message);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        return builder.ToString();
    }

    private static void Write(XmlWriter writer, PaxlstMessage message)
    {
        writer.WriteStartElement("Message");
        WriteBody(writer, message);
        writer.WriteEndElement();
    }

    private static void WriteBody(XmlWriter writer, PaxlstMessage message)
    {
        if (message.ReportingParty is { } party)
        {
            writer.WriteStartElement("ReportingParty");
            if (!string.IsNullOrEmpty(party.Name)) writer.WriteAttributeString("Name", party.Name);
            if (!string.IsNullOrEmpty(party.ContactNumber)) writer.WriteAttributeString("ContactNumber", party.ContactNumber);
            writer.WriteEndElement();
        }

        writer.WriteStartElement("Flight");
        WriteAttribute(writer, "ID", message.Flight.Id);
        WriteAttribute(writer, "CarrierName", message.Flight.CarrierName);
        WriteAttribute(writer, "CarrierCode", message.Flight.CarrierCode);
        WriteAttribute(writer, "Port", message.Flight.Port);
        WriteAttribute(writer, "DeparturePort", message.Flight.DeparturePort);
        WriteAttribute(writer, "ArrivalPort", message.Flight.ArrivalPort);
        WriteAttribute(writer, "Date", message.Flight.ArrivalDate is { } arrival
            ? arrival.ToString(DateFormat)
            : message.Flight.DepartureDate?.ToString(DateFormat));

        writer.WriteStartElement("Travellers");
        foreach (var traveller in message.Travellers)
        {
            writer.WriteStartElement("Traveller");
            writer.WriteAttributeString("Type", traveller.Type == PaxlstTravellerType.Crew ? "CREW" : "PAX");
            WriteElement(writer, "LastName", traveller.Surname);
            WriteElement(writer, "FirstName", traveller.FirstName);
            WriteElement(writer, "MiddleName", traveller.MiddleName);
            WriteElement(writer, "Nationality", traveller.Nationality);
            WriteElement(writer, "Sex", traveller.Sex);
            WriteElement(writer, "DateOfBirth", traveller.DateOfBirth?.ToString(DateFormat));
            WriteElement(writer, "DocType", traveller.DocumentType);
            WriteElement(writer, "PassportNumber", traveller.DocumentNumber);
            WriteElement(writer, "DateOfExpiry", traveller.ExpiryDate?.ToString(DateFormat));
            WriteElement(writer, "CountryOfIssue", traveller.CountryOfIssue);
            WriteElement(writer, "CountryOfResidence", traveller.CountryOfResidence);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        if (message.ControlTotals.Count > 0)
        {
            writer.WriteStartElement("ControlTotals");
            foreach (var total in message.ControlTotals)
            {
                writer.WriteStartElement("ControlTotal");
                writer.WriteAttributeString("Type", total.Qualifier);
                writer.WriteAttributeString("Value", total.Value.ToString());
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteAttribute(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) writer.WriteAttributeString(name, value);
    }

    private static void WriteElement(XmlWriter writer, string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        writer.WriteElementString(name, value);
    }
}

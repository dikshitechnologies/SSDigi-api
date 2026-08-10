using System.Text.Json.Serialization;

namespace JEWELLBISREACT.Model
{
    public class ItemCreation
    {
        public string FitemCode { get; set; }
        public string FitemName { get; set; }
        public string GroupName { get; set; }

        public string GstNumber { get; set; }
        public string Counter { get; set; }
        public string Prefix { get; set; }
        public string ShortName { get; set; }
        public string HsnCode { get; set; }

        // Existing piece rate field (fPieceRate in DB)
        public string pieceRate { get; set; }

        public string gst { get; set; }
        public string manualprefix { get; set; }
        public string FWastage { get; set; }
        public string fMc { get; set; }
        public string fDivision { get; set; }

        // --- 5 new fields ---

        // Tax on piece rate (fTaxPieceRate) — maps to itemnameparenttax
        public string fTaxPieceRate { get; set; }

        // Availability flag (Flag) — Y/N
        public string Flag { get; set; }

        // Units (fUnits)
        public string fUnits { get; set; }

        // Quantity (fQty)
        public string fQty { get; set; }

        // Company code (fComp)
        public string fComp { get; set; }
    }
}

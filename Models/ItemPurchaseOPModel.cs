namespace JEWELLBISREACT.Model
{
    /// <summary>
    /// Multipart/form-data payload for POST Save and PUT Update.
    /// Property names match the form field labels exactly.
    /// Lookup-only fields (names, tax, metal type, etc.) are resolved
    /// server-side from the code fields — do NOT send them in the form.
    /// </summary>
    public class ItemPurchaseOPModel
    {
        // ── Voucher / Barcode ──────────────────────────────────────────
        public string? RefNo       { get; set; }   // Voucher  e.g. OP000063AA
        public string? Barcode     { get; set; }   // fPrefix  e.g. AADMMH

        // ── Item ──────────────────────────────────────────────────────
        public string? ItemCode    { get; set; }   // Itemcode e.g. 00146
        public string? HuidNo      { get; set; }   // FHUID / fCertificate
        public string? DesignCode  { get; set; }   // fDesign
        public string? SectionCode { get; set; }   // fSection
        public string? SizeCode    { get; set; }   // fSize
        public string? CounterCode { get; set; }   // fBox  (Location)
        public string? DivisionCode{ get; set; }   // fDiv  (Purity)
        public string? Parent      { get; set; }   // fParent

        // ── Weights & Charges ─────────────────────────────────────────
        public decimal Pcs        { get; set; }    // Qty
        public decimal GrossWt    { get; set; }    // Gross
        public decimal LessWt     { get; set; }    // StnWt
        public decimal NetWt      { get; set; }    // Gms
        public decimal VA         { get; set; }    // Wastage
        public decimal Making     { get; set; }    // McAmount
        public decimal StoneChg   { get; set; }    // StnChrg
        public decimal Others     { get; set; }    // fOthers

        // ── Narration / Status ────────────────────────────────────────
        public string? ShortNarr  { get; set; }    // Narration
        public string? LongNarr   { get; set; }    // fDescription
        public string? InOutStock { get; set; }    // fInOutStock  e.g. InStock

        // ── Images (filenames – UPDATE only, ignored on POST) ─────────
        public string? Image1     { get; set; }    // fImage1
        public string? Image2     { get; set; }    // fImage2
        public string? Image3     { get; set; }    // fImage3
        public string? Image4     { get; set; }    // fImage4
    }
}

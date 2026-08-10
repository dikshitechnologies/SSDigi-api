namespace CHITSCHEME.Global
{
    public class PriceCalculator
    {

        public static (decimal TotalAmount, decimal TaxAmount, decimal TodayRate, decimal TotalWastage, decimal TotalWeightWithWastage)
        CalculatePrice(string pieceRate, decimal netWt, decimal fVA, decimal fVAGMS, decimal fRate, decimal fMc, decimal fOthers, decimal fStoneCharges, decimal fTax, decimal goldRate)
        {
            // Calculate Wastage
            decimal totalWastage = (fVAGMS > 0) ? fVAGMS : ((fVA > 0) ? (netWt * fVA / 100) : 0);
            decimal totalWeightWithWastage = netWt + totalWastage;

            // Calculate Rate (if piece rate is not 'Y', calculate based on weight and gold rate)
            decimal todayRate = (pieceRate == "Y") ? fRate : (totalWeightWithWastage * goldRate);

            // Calculate Total Amount
            decimal totalAmount = (pieceRate == "Y")
                ? (fRate + fMc + fOthers + fStoneCharges)
                : (todayRate + fMc + fOthers + fStoneCharges);

            // Calculate Tax Amount
            decimal taxAmount = (fTax > 0) ? (totalAmount * fTax / 100) : 0;
            totalAmount += taxAmount;

            return (totalAmount, taxAmount, todayRate, totalWastage, totalWeightWithWastage);
        }
    }
}

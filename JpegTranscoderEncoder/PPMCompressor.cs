using System;

namespace JpegTranscoderEncoder
{
    public class PpmCompressor
    {
        private const int ModelOrder = 1;
        private const int EscapeSymbol = 256;
        private ArithmeticEncoder enc;
        private PpmModel model;
        int[] history;

        public PpmCompressor(BitOutputStream outStream)
        {
            this.model = new PpmModel(ModelOrder, EscapeSymbol);
            this.enc = new ArithmeticEncoder(outStream);
            history = new int[0];
        }

        public void Compress(int symbol)
        {
                EncodeSymbol(model, history, symbol, enc);
                model.IncrementContexts(history, symbol);

                if (model.ModelOrder >= 1)
                {
                    if (history.Length < model.ModelOrder)
                        Array.Resize(ref history, history.Length + 1);
                    Array.Copy(history, 0, history, 1, history.Length - 1);
                    history[0] = symbol;
                }

        }

        public void Finish()
        {
            EncodeSymbol(model, history, EscapeSymbol, enc);
            enc.Finish();
        }


        private static void EncodeSymbol(PpmModel model, int[] history, int symbol, ArithmeticEncoder enc)
        {
            var order = history.Length;

            while (order >= 0)
            {
                var isBreak = false;
                var ctx = model.RootContext;
                for (var i = 0; i < order; i++)
                {
                    ctx = ctx.Subcontexts[history[i]];
                    if (ctx == null)
                    {
                        order--;
                        isBreak = true;
                        break;
                    }
                }

                if (isBreak)
                    continue;

                if (symbol != EscapeSymbol && ctx.Frequencies.Get(symbol) > 0)
                {
                    enc.Write(ctx.Frequencies, symbol);
                    return;
                }

                enc.Write(ctx.Frequencies, EscapeSymbol);
                order--;
            }

            enc.Write(model.orderMinus1Freqs, symbol);
        }
    }
}
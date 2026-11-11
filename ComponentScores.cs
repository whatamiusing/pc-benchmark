using System;
using System.Linq;

namespace WaiuBenchmark
{
    internal static class ComponentScores
    {
        internal const int MetricCount=10;
        internal static void Record(Result result,StorageBenchmark storage,double? glassTime)
        {
            result.Metrics=new[] { result.Single,result.Parallel,result.Compression,result.Memory,
                result.Latency[result.Latency.Length-1],result.GraphicsTime.GetValueOrDefault(),glassTime.GetValueOrDefault(),
                storage.Read.GetValueOrDefault(),storage.Write.GetValueOrDefault(),storage.RandomRead.GetValueOrDefault() };
            result.StorageDrive=storage.Drive;
        }

        internal static double?[] Compare(Result result,Result baseline)
        {
            var scores=new double?[5];
            if (baseline==null || result.Metrics==null || baseline.Metrics==null) return scores;
            scores[1]=Group(result.Metrics,baseline.Metrics,0,3);
            scores[2]=Group(result.Metrics,baseline.Metrics,3,2);
            scores[3]=Group(result.Metrics,baseline.Metrics,5,2);
            if (!string.IsNullOrEmpty(result.StorageDrive) &&
                string.Equals(result.StorageDrive,baseline.StorageDrive,StringComparison.OrdinalIgnoreCase))
                scores[4]=Group(result.Metrics,baseline.Metrics,7,3);
            if (scores.Skip(1).All(score=>score.HasValue))
                scores[0]=Math.Exp(scores.Skip(1).Average(score=>Math.Log(score.Value)));
            return scores;
        }

        private static double? Group(double[] current,double[] previous,int start,int count)
        {
            double sum=0;
            for (int i=start;i<start+count;i++)
            {
                if (current[i]<=0 || previous[i]<=0) return null;
                double log=Math.Log(current[i])-Math.Log(previous[i]);
                sum+=(i>=4 && i<=6) ? -log : log;
            }
            double result=Math.Exp(sum/count);
            return result>0 && !double.IsInfinity(result) ? (double?)result : null;
        }
    }
}

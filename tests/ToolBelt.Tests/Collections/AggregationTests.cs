using System;
using System.Collections.Generic;
using System.Linq;
using ToolBelt.Collections;
using ToolBelt.Tests.Framework;

namespace ToolBelt.Tests.Collections
{
    public sealed class AggregationTests
    {
        private sealed class Sale { public string Region = ""; public string Product = ""; public double Amount; }

        private static readonly Sale[] Sales =
        {
            new Sale { Region = "EU", Product = "A", Amount = 10 },
            new Sale { Region = "EU", Product = "B", Amount = 20 },
            new Sale { Region = "US", Product = "A", Amount = 30 },
            new Sale { Region = "EU", Product = "A", Amount = 40 },
        };

        public void SummarizeByKey()
        {
            var summary = Aggregation.Summarize(Sales, s => s.Region, s => s.Amount);
            Check.Equal(2, summary.Count);

            var eu = summary.First(g => g.Key == "EU");
            Check.Equal(3, eu.Count);
            Check.Close(70, eu.Sum, 1e-9);
            Check.Close(70.0 / 3, eu.Mean, 1e-9);
            Check.Close(10, eu.Min, 1e-9);
            Check.Close(40, eu.Max, 1e-9);

            var us = summary.First(g => g.Key == "US");
            Check.Equal(1, us.Count);
            Check.Close(0, us.StandardDeviation, 1e-9); // single element
        }

        public void SummarizePreservesFirstSeenOrder()
        {
            var summary = Aggregation.Summarize(Sales, s => s.Region, s => s.Amount);
            Check.Equal("EU", summary[0].Key);
            Check.Equal("US", summary[1].Key);
        }

        public void StandardDeviationMatchesFormula()
        {
            var items = new[] { 2.0, 4.0, 6.0 };
            var summary = Aggregation.Summarize(items, _ => "g", v => v);
            // sample variance of {2,4,6} = ((2-4)^2+(4-4)^2+(6-4)^2)/2 = 4 -> sd 2
            Check.Close(2, summary[0].StandardDeviation, 1e-9);
        }

        public void Pivot()
        {
            var grid = Aggregation.Pivot(Sales, s => s.Region, s => s.Product, s => s.Amount, vals => vals.Sum());
            Check.Close(50, grid["EU"]["A"], 1e-9); // 10 + 40
            Check.Close(20, grid["EU"]["B"], 1e-9);
            Check.Close(30, grid["US"]["A"], 1e-9);
            Check.False(grid["US"].ContainsKey("B")); // no US/B sales
        }

        public void NullArguments_Throw()
        {
            Check.Throws<ArgumentNullException>(() => Aggregation.Summarize<int, int>(null!, x => x, x => x));
        }
    }
}

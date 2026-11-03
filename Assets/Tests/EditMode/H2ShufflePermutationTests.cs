using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TimeTheft.Tests
{
    public sealed class H2ShufflePermutationTests
    {
        private sealed class CountRandom : Random
        {
            public int Calls;
            public CountRandom(int seed):base(seed){}
            public override int Next(int max){Calls++;return base.Next(max);}
        }
        public sealed class ForcedRandom : Random
        {
            private readonly int[] draws;
            public bool Closing;
            public int CloseCalls;
            public ForcedRandom(int[] desired):base(17)
            {
                var ids=new[]{0,1,2,3,4};var q=new List<int>();
                for(int i=4;i>0;i--){int j=Array.IndexOf(ids,desired[i],0,i+1);q.Add(j);int s=ids[i];ids[i]=ids[j];ids[j]=s;}draws=q.ToArray();
            }
            public override int Next(int max)=>Closing?draws[CloseCalls++]:base.Next(max);
        }
        public static IEnumerable<int[]> Permutations(int[] order=null,int start=0)
        {
            order=order??new[]{0,1,2,3,4};
            if(start==order.Length){yield return (int[])order.Clone();yield break;}
            for(int i=start;i<order.Length;i++)
            {int s=order[start];order[start]=order[i];order[i]=s;foreach(var p in Permutations(order,start+1))yield return p;s=order[start];order[start]=order[i];order[i]=s;}
        }
        private static GameRules Rules(bool duplicates=false)=>new GameRules{InitialItemCount=0,ItemsPerBatch=0,MinimumHours=duplicates?3:1,MaximumHours=duplicates?3:12};
        private static void Swap<T>(T[] a,int i,int j){T v=a[i];a[i]=a[j];a[j]=v;}

        [Test] public void ExistingSeededDealShuffleAndFollowingRandomStreamStayIdentical()
        {
            foreach(bool duplicate in new[]{false,true}) for(int seed=0;seed<128;seed++) for(int chosen=0;chosen<5;chosen++)
            {
                var rules=Rules(duplicate);var rng=new CountRandom(seed);var oracle=new Random(seed);
                var expected=new ClockFace[5];
                for(int i=0;i<5;i++)
                {
                    var kind=i<2?ClockKind.Normal:i==2?ClockKind.Red:oracle.NextDouble()<rules.ExtraRedProbability?ClockKind.Red:ClockKind.Normal;
                    expected[i]=new ClockFace(oracle.Next(rules.MinimumHours,rules.MaximumHours+1),kind);
                }
                for(int i=4;i>0;i--)Swap(expected,i,oracle.Next(i+1));
                var match=new TimeTheftMatch(rules,rng,new Random(89));match.BeginBatch();
                for(int i=0;i<5;i++)Assert.That(match.GetClock(i).Face.Value,Is.EqualTo(expected[i]));
                Assert.That(match.LastShuffle,Is.Null);int count=rng.Calls;
                int[] source={0,1,2,3,4};for(int i=4;i>0;i--){int j=oracle.Next(i+1);Swap(expected,i,j);Swap(source,i,j);}
                Assert.That(match.CloseAndShuffle(),Is.True);Assert.That(rng.Calls-count,Is.EqualTo(4));
                Assert.That(match.LastShuffle.DestinationToSource,Is.EqualTo(source));
                Assert.That(rng.Next(),Is.EqualTo(oracle.Next()),"연출 기록이 뒤의 게임 난수열을 바꾸지 않는다");
                match.CompleteShuffle();match.SelectClock(Participant.Player,chosen);match.CommitAction(Participant.Player,TimeAction.Ration);
                Assert.That(match.GetClock(chosen).Face.Value,Is.EqualTo(expected[chosen]));
            }
        }
        [Test] public void All120MappingsUseStableSourcesAndImmutableInverseWithoutExtraDraws()
        {
            int count=0;
            foreach(var desired in Permutations())
            {
                var rng=new ForcedRandom(desired);var match=new TimeTheftMatch(Rules(true),rng,new Random(3));match.BeginBatch();rng.Closing=true;
                match.CloseAndShuffle();var result=match.LastShuffle;
                Assert.That(result.DestinationToSource,Is.EqualTo(desired));Assert.That(rng.CloseCalls,Is.EqualTo(4));
                for(int d=0;d<5;d++)Assert.That(result.SourceToDestination[result.DestinationToSource[d]],Is.EqualTo(d));
                Assert.Throws<NotSupportedException>(()=>((IList<int>)result.DestinationToSource)[0]=4);
                Assert.That(match.CloseAndShuffle(),Is.False);Assert.That(match.LastShuffle,Is.SameAs(result));Assert.That(rng.CloseCalls,Is.EqualTo(4));count++;
            }
            Assert.That(count,Is.EqualTo(120));
            Assert.That(typeof(AiItemObservation).GetProperties().Any(p=>p.PropertyType==typeof(ClockShuffle)),Is.False);
        }
    }
}

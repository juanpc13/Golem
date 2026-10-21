using System.Reflection;
using Choreography.Theater;

namespace GolemAPI;

// The golem's Performance. Initialization is versioned INSIDE the actor through the
// hydration hooks (hosting-environments guide, "Seed / migration idiom"): OnHydrated
// runs after every hydration and issues the release chain; applied releases skip,
// new ones run and journal. To evolve the golem, append the next release below —
// never edit an applied one (its body signature is guarded).
internal sealed class GolemPerformance : PerformanceV2
{
    /// <summary>THE MAZE'S RELEASE (propuesta 108): the text <c>Catalog.Maze().AsRelease(staged: true)</c> renders — kept here verbatim, like the
    /// other maps', and held equal to it by <c>MazeReleaseTests</c>. A journal keeps the map; the code keeps the recipe (the drawing).</summary>
    internal const string MazeRelease = @"upgrade('maze_v1') {
    maze = MapLayout('maze');
    {
        cell_1_19 = maze.Area('cell-1-19').At(Position(1.5, 28.5)).Size(1.5, 1.5);
        cell_2_19 = maze.Area('cell-2-19').At(Position(3.0, 28.5)).Size(1.5, 1.5);
        cell_3_19 = maze.Area('cell-3-19').At(Position(4.5, 28.5)).Size(1.5, 1.5);
        cell_4_19 = maze.Area('cell-4-19').At(Position(6.0, 28.5)).Size(1.5, 1.5);
        cell_5_19 = maze.Area('cell-5-19').At(Position(7.5, 28.5)).Size(1.5, 1.5);
        cell_6_19 = maze.Area('cell-6-19').At(Position(9.0, 28.5)).Size(1.5, 1.5);
        cell_7_19 = maze.Area('cell-7-19').At(Position(10.5, 28.5)).Size(1.5, 1.5);
        cell_8_19 = maze.Area('cell-8-19').At(Position(12.0, 28.5)).Size(1.5, 1.5);
        cell_10_19 = maze.Area('cell-10-19').At(Position(15.0, 28.5)).Size(1.5, 1.5);
        cell_11_19 = maze.Area('cell-11-19').At(Position(16.5, 28.5)).Size(1.5, 1.5);
        cell_12_19 = maze.Area('cell-12-19').At(Position(18.0, 28.5)).Size(1.5, 1.5);
        cell_13_19 = maze.Area('cell-13-19').At(Position(19.5, 28.5)).Size(1.5, 1.5);
        cell_14_19 = maze.Area('cell-14-19').At(Position(21.0, 28.5)).Size(1.5, 1.5);
        cell_15_19 = maze.Area('cell-15-19').At(Position(22.5, 28.5)).Size(1.5, 1.5);
        cell_16_19 = maze.Area('cell-16-19').At(Position(24.0, 28.5)).Size(1.5, 1.5);
        cell_17_19 = maze.Area('cell-17-19').At(Position(25.5, 28.5)).Size(1.5, 1.5);
        cell_1_18 = maze.Area('cell-1-18').At(Position(1.5, 27.0)).Size(1.5, 1.5);
        cell_4_18 = maze.Area('cell-4-18').At(Position(6.0, 27.0)).Size(1.5, 1.5);
        cell_8_18 = maze.Area('cell-8-18').At(Position(12.0, 27.0)).Size(1.5, 1.5);
        cell_10_18 = maze.Area('cell-10-18').At(Position(15.0, 27.0)).Size(1.5, 1.5);
        cell_14_18 = maze.Area('cell-14-18').At(Position(21.0, 27.0)).Size(1.5, 1.5);
        cell_17_18 = maze.Area('cell-17-18').At(Position(25.5, 27.0)).Size(1.5, 1.5);
        cell_1_17 = maze.Area('cell-1-17').At(Position(1.5, 25.5)).Size(1.5, 1.5);
        cell_2_17 = maze.Area('cell-2-17').At(Position(3.0, 25.5)).Size(1.5, 1.5);
        cell_3_17 = maze.Area('cell-3-17').At(Position(4.5, 25.5)).Size(1.5, 1.5);
        cell_4_17 = maze.Area('cell-4-17').At(Position(6.0, 25.5)).Size(1.5, 1.5);
        cell_5_17 = maze.Area('cell-5-17').At(Position(7.5, 25.5)).Size(1.5, 1.5);
        cell_6_17 = maze.Area('cell-6-17').At(Position(9.0, 25.5)).Size(1.5, 1.5);
        cell_7_17 = maze.Area('cell-7-17').At(Position(10.5, 25.5)).Size(1.5, 1.5);
        cell_8_17 = maze.Area('cell-8-17').At(Position(12.0, 25.5)).Size(1.5, 1.5);
        cell_9_17 = maze.Area('cell-9-17').At(Position(13.5, 25.5)).Size(1.5, 1.5);
        cell_10_17 = maze.Area('cell-10-17').At(Position(15.0, 25.5)).Size(1.5, 1.5);
        cell_11_17 = maze.Area('cell-11-17').At(Position(16.5, 25.5)).Size(1.5, 1.5);
        cell_12_17 = maze.Area('cell-12-17').At(Position(18.0, 25.5)).Size(1.5, 1.5);
        cell_13_17 = maze.Area('cell-13-17').At(Position(19.5, 25.5)).Size(1.5, 1.5);
        cell_14_17 = maze.Area('cell-14-17').At(Position(21.0, 25.5)).Size(1.5, 1.5);
        cell_15_17 = maze.Area('cell-15-17').At(Position(22.5, 25.5)).Size(1.5, 1.5);
        cell_16_17 = maze.Area('cell-16-17').At(Position(24.0, 25.5)).Size(1.5, 1.5);
        cell_17_17 = maze.Area('cell-17-17').At(Position(25.5, 25.5)).Size(1.5, 1.5);
        cell_1_16 = maze.Area('cell-1-16').At(Position(1.5, 24.0)).Size(1.5, 1.5);
        cell_4_16 = maze.Area('cell-4-16').At(Position(6.0, 24.0)).Size(1.5, 1.5);
        cell_6_16 = maze.Area('cell-6-16').At(Position(9.0, 24.0)).Size(1.5, 1.5);
        cell_12_16 = maze.Area('cell-12-16').At(Position(18.0, 24.0)).Size(1.5, 1.5);
        cell_14_16 = maze.Area('cell-14-16').At(Position(21.0, 24.0)).Size(1.5, 1.5);
        cell_17_16 = maze.Area('cell-17-16').At(Position(25.5, 24.0)).Size(1.5, 1.5);
        cell_1_15 = maze.Area('cell-1-15').At(Position(1.5, 22.5)).Size(1.5, 1.5);
        cell_2_15 = maze.Area('cell-2-15').At(Position(3.0, 22.5)).Size(1.5, 1.5);
        cell_3_15 = maze.Area('cell-3-15').At(Position(4.5, 22.5)).Size(1.5, 1.5);
        cell_4_15 = maze.Area('cell-4-15').At(Position(6.0, 22.5)).Size(1.5, 1.5);
        cell_6_15 = maze.Area('cell-6-15').At(Position(9.0, 22.5)).Size(1.5, 1.5);
        cell_7_15 = maze.Area('cell-7-15').At(Position(10.5, 22.5)).Size(1.5, 1.5);
        cell_8_15 = maze.Area('cell-8-15').At(Position(12.0, 22.5)).Size(1.5, 1.5);
        cell_10_15 = maze.Area('cell-10-15').At(Position(15.0, 22.5)).Size(1.5, 1.5);
        cell_11_15 = maze.Area('cell-11-15').At(Position(16.5, 22.5)).Size(1.5, 1.5);
        cell_12_15 = maze.Area('cell-12-15').At(Position(18.0, 22.5)).Size(1.5, 1.5);
        cell_14_15 = maze.Area('cell-14-15').At(Position(21.0, 22.5)).Size(1.5, 1.5);
        cell_15_15 = maze.Area('cell-15-15').At(Position(22.5, 22.5)).Size(1.5, 1.5);
        cell_16_15 = maze.Area('cell-16-15').At(Position(24.0, 22.5)).Size(1.5, 1.5);
        cell_17_15 = maze.Area('cell-17-15').At(Position(25.5, 22.5)).Size(1.5, 1.5);
        cell_4_14 = maze.Area('cell-4-14').At(Position(6.0, 21.0)).Size(1.5, 1.5);
        cell_8_14 = maze.Area('cell-8-14').At(Position(12.0, 21.0)).Size(1.5, 1.5);
        cell_10_14 = maze.Area('cell-10-14').At(Position(15.0, 21.0)).Size(1.5, 1.5);
        cell_14_14 = maze.Area('cell-14-14').At(Position(21.0, 21.0)).Size(1.5, 1.5);
        cell_1_13 = maze.Area('cell-1-13').At(Position(1.5, 19.5)).Size(1.5, 1.5);
        cell_2_13 = maze.Area('cell-2-13').At(Position(3.0, 19.5)).Size(1.5, 1.5);
        cell_3_13 = maze.Area('cell-3-13').At(Position(4.5, 19.5)).Size(1.5, 1.5);
        cell_4_13 = maze.Area('cell-4-13').At(Position(6.0, 19.5)).Size(1.5, 1.5);
        cell_5_13 = maze.Area('cell-5-13').At(Position(7.5, 19.5)).Size(1.5, 1.5);
        cell_6_13 = maze.Area('cell-6-13').At(Position(9.0, 19.5)).Size(1.5, 1.5);
        cell_8_13 = maze.Area('cell-8-13').At(Position(12.0, 19.5)).Size(1.5, 1.5);
        cell_10_13 = maze.Area('cell-10-13').At(Position(15.0, 19.5)).Size(1.5, 1.5);
        cell_12_13 = maze.Area('cell-12-13').At(Position(18.0, 19.5)).Size(1.5, 1.5);
        cell_13_13 = maze.Area('cell-13-13').At(Position(19.5, 19.5)).Size(1.5, 1.5);
        cell_14_13 = maze.Area('cell-14-13').At(Position(21.0, 19.5)).Size(1.5, 1.5);
        cell_15_13 = maze.Area('cell-15-13').At(Position(22.5, 19.5)).Size(1.5, 1.5);
        cell_16_13 = maze.Area('cell-16-13').At(Position(24.0, 19.5)).Size(1.5, 1.5);
        cell_17_13 = maze.Area('cell-17-13').At(Position(25.5, 19.5)).Size(1.5, 1.5);
        cell_1_12 = maze.Area('cell-1-12').At(Position(1.5, 18.0)).Size(1.5, 1.5);
        cell_6_12 = maze.Area('cell-6-12').At(Position(9.0, 18.0)).Size(1.5, 1.5);
        cell_8_12 = maze.Area('cell-8-12').At(Position(12.0, 18.0)).Size(1.5, 1.5);
        cell_9_12 = maze.Area('cell-9-12').At(Position(13.5, 18.0)).Size(1.5, 1.5);
        cell_10_12 = maze.Area('cell-10-12').At(Position(15.0, 18.0)).Size(1.5, 1.5);
        cell_12_12 = maze.Area('cell-12-12').At(Position(18.0, 18.0)).Size(1.5, 1.5);
        cell_17_12 = maze.Area('cell-17-12').At(Position(25.5, 18.0)).Size(1.5, 1.5);
        cell_1_11 = maze.Area('cell-1-11').At(Position(1.5, 16.5)).Size(1.5, 1.5);
        cell_2_11 = maze.Area('cell-2-11').At(Position(3.0, 16.5)).Size(1.5, 1.5);
        cell_3_11 = maze.Area('cell-3-11').At(Position(4.5, 16.5)).Size(1.5, 1.5);
        cell_4_11 = maze.Area('cell-4-11').At(Position(6.0, 16.5)).Size(1.5, 1.5);
        cell_5_11 = maze.Area('cell-5-11').At(Position(7.5, 16.5)).Size(1.5, 1.5);
        cell_6_11 = maze.Area('cell-6-11').At(Position(9.0, 16.5)).Size(1.5, 1.5);
        cell_8_11 = maze.Area('cell-8-11').At(Position(12.0, 16.5)).Size(1.5, 1.5);
        cell_9_11 = maze.Area('cell-9-11').At(Position(13.5, 16.5)).Size(1.5, 1.5);
        cell_10_11 = maze.Area('cell-10-11').At(Position(15.0, 16.5)).Size(1.5, 1.5);
        cell_12_11 = maze.Area('cell-12-11').At(Position(18.0, 16.5)).Size(1.5, 1.5);
        cell_13_11 = maze.Area('cell-13-11').At(Position(19.5, 16.5)).Size(1.5, 1.5);
        cell_14_11 = maze.Area('cell-14-11').At(Position(21.0, 16.5)).Size(1.5, 1.5);
        cell_15_11 = maze.Area('cell-15-11').At(Position(22.5, 16.5)).Size(1.5, 1.5);
        cell_16_11 = maze.Area('cell-16-11').At(Position(24.0, 16.5)).Size(1.5, 1.5);
        cell_17_11 = maze.Area('cell-17-11').At(Position(25.5, 16.5)).Size(1.5, 1.5);
        cell_1_10 = maze.Area('cell-1-10').At(Position(1.5, 15.0)).Size(1.5, 1.5);
        cell_6_10 = maze.Area('cell-6-10').At(Position(9.0, 15.0)).Size(1.5, 1.5);
        cell_12_10 = maze.Area('cell-12-10').At(Position(18.0, 15.0)).Size(1.5, 1.5);
        cell_17_10 = maze.Area('cell-17-10').At(Position(25.5, 15.0)).Size(1.5, 1.5);
        cell_1_9 = maze.Area('cell-1-9').At(Position(1.5, 13.5)).Size(1.5, 1.5);
        cell_2_9 = maze.Area('cell-2-9').At(Position(3.0, 13.5)).Size(1.5, 1.5);
        cell_3_9 = maze.Area('cell-3-9').At(Position(4.5, 13.5)).Size(1.5, 1.5);
        cell_4_9 = maze.Area('cell-4-9').At(Position(6.0, 13.5)).Size(1.5, 1.5);
        cell_5_9 = maze.Area('cell-5-9').At(Position(7.5, 13.5)).Size(1.5, 1.5);
        cell_6_9 = maze.Area('cell-6-9').At(Position(9.0, 13.5)).Size(1.5, 1.5);
        cell_7_9 = maze.Area('cell-7-9').At(Position(10.5, 13.5)).Size(1.5, 1.5);
        cell_8_9 = maze.Area('cell-8-9').At(Position(12.0, 13.5)).Size(1.5, 1.5);
        cell_10_9 = maze.Area('cell-10-9').At(Position(15.0, 13.5)).Size(1.5, 1.5);
        cell_11_9 = maze.Area('cell-11-9').At(Position(16.5, 13.5)).Size(1.5, 1.5);
        cell_12_9 = maze.Area('cell-12-9').At(Position(18.0, 13.5)).Size(1.5, 1.5);
        cell_13_9 = maze.Area('cell-13-9').At(Position(19.5, 13.5)).Size(1.5, 1.5);
        cell_14_9 = maze.Area('cell-14-9').At(Position(21.0, 13.5)).Size(1.5, 1.5);
        cell_15_9 = maze.Area('cell-15-9').At(Position(22.5, 13.5)).Size(1.5, 1.5);
        cell_16_9 = maze.Area('cell-16-9').At(Position(24.0, 13.5)).Size(1.5, 1.5);
        cell_17_9 = maze.Area('cell-17-9').At(Position(25.5, 13.5)).Size(1.5, 1.5);
        cell_4_8 = maze.Area('cell-4-8').At(Position(6.0, 12.0)).Size(1.5, 1.5);
        cell_8_8 = maze.Area('cell-8-8').At(Position(12.0, 12.0)).Size(1.5, 1.5);
        cell_10_8 = maze.Area('cell-10-8').At(Position(15.0, 12.0)).Size(1.5, 1.5);
        cell_14_8 = maze.Area('cell-14-8').At(Position(21.0, 12.0)).Size(1.5, 1.5);
        cell_1_7 = maze.Area('cell-1-7').At(Position(1.5, 10.5)).Size(1.5, 1.5);
        cell_2_7 = maze.Area('cell-2-7').At(Position(3.0, 10.5)).Size(1.5, 1.5);
        cell_3_7 = maze.Area('cell-3-7').At(Position(4.5, 10.5)).Size(1.5, 1.5);
        cell_4_7 = maze.Area('cell-4-7').At(Position(6.0, 10.5)).Size(1.5, 1.5);
        cell_5_7 = maze.Area('cell-5-7').At(Position(7.5, 10.5)).Size(1.5, 1.5);
        cell_6_7 = maze.Area('cell-6-7').At(Position(9.0, 10.5)).Size(1.5, 1.5);
        cell_7_7 = maze.Area('cell-7-7').At(Position(10.5, 10.5)).Size(1.5, 1.5);
        cell_8_7 = maze.Area('cell-8-7').At(Position(12.0, 10.5)).Size(1.5, 1.5);
        cell_10_7 = maze.Area('cell-10-7').At(Position(15.0, 10.5)).Size(1.5, 1.5);
        cell_11_7 = maze.Area('cell-11-7').At(Position(16.5, 10.5)).Size(1.5, 1.5);
        cell_12_7 = maze.Area('cell-12-7').At(Position(18.0, 10.5)).Size(1.5, 1.5);
        cell_13_7 = maze.Area('cell-13-7').At(Position(19.5, 10.5)).Size(1.5, 1.5);
        cell_14_7 = maze.Area('cell-14-7').At(Position(21.0, 10.5)).Size(1.5, 1.5);
        cell_15_7 = maze.Area('cell-15-7').At(Position(22.5, 10.5)).Size(1.5, 1.5);
        cell_16_7 = maze.Area('cell-16-7').At(Position(24.0, 10.5)).Size(1.5, 1.5);
        cell_17_7 = maze.Area('cell-17-7').At(Position(25.5, 10.5)).Size(1.5, 1.5);
        cell_1_6 = maze.Area('cell-1-6').At(Position(1.5, 9.0)).Size(1.5, 1.5);
        cell_4_6 = maze.Area('cell-4-6').At(Position(6.0, 9.0)).Size(1.5, 1.5);
        cell_8_6 = maze.Area('cell-8-6').At(Position(12.0, 9.0)).Size(1.5, 1.5);
        cell_10_6 = maze.Area('cell-10-6').At(Position(15.0, 9.0)).Size(1.5, 1.5);
        cell_14_6 = maze.Area('cell-14-6').At(Position(21.0, 9.0)).Size(1.5, 1.5);
        cell_17_6 = maze.Area('cell-17-6').At(Position(25.5, 9.0)).Size(1.5, 1.5);
        cell_1_5 = maze.Area('cell-1-5').At(Position(1.5, 7.5)).Size(1.5, 1.5);
        cell_2_5 = maze.Area('cell-2-5').At(Position(3.0, 7.5)).Size(1.5, 1.5);
        cell_4_5 = maze.Area('cell-4-5').At(Position(6.0, 7.5)).Size(1.5, 1.5);
        cell_5_5 = maze.Area('cell-5-5').At(Position(7.5, 7.5)).Size(1.5, 1.5);
        cell_6_5 = maze.Area('cell-6-5').At(Position(9.0, 7.5)).Size(1.5, 1.5);
        cell_7_5 = maze.Area('cell-7-5').At(Position(10.5, 7.5)).Size(1.5, 1.5);
        cell_8_5 = maze.Area('cell-8-5').At(Position(12.0, 7.5)).Size(1.5, 1.5);
        cell_9_5 = maze.Area('cell-9-5').At(Position(13.5, 7.5)).Size(1.5, 1.5);
        cell_10_5 = maze.Area('cell-10-5').At(Position(15.0, 7.5)).Size(1.5, 1.5);
        cell_11_5 = maze.Area('cell-11-5').At(Position(16.5, 7.5)).Size(1.5, 1.5);
        cell_12_5 = maze.Area('cell-12-5').At(Position(18.0, 7.5)).Size(1.5, 1.5);
        cell_13_5 = maze.Area('cell-13-5').At(Position(19.5, 7.5)).Size(1.5, 1.5);
        cell_14_5 = maze.Area('cell-14-5').At(Position(21.0, 7.5)).Size(1.5, 1.5);
        cell_16_5 = maze.Area('cell-16-5').At(Position(24.0, 7.5)).Size(1.5, 1.5);
        cell_17_5 = maze.Area('cell-17-5').At(Position(25.5, 7.5)).Size(1.5, 1.5);
        cell_2_4 = maze.Area('cell-2-4').At(Position(3.0, 6.0)).Size(1.5, 1.5);
        cell_4_4 = maze.Area('cell-4-4').At(Position(6.0, 6.0)).Size(1.5, 1.5);
        cell_6_4 = maze.Area('cell-6-4').At(Position(9.0, 6.0)).Size(1.5, 1.5);
        cell_12_4 = maze.Area('cell-12-4').At(Position(18.0, 6.0)).Size(1.5, 1.5);
        cell_14_4 = maze.Area('cell-14-4').At(Position(21.0, 6.0)).Size(1.5, 1.5);
        cell_16_4 = maze.Area('cell-16-4').At(Position(24.0, 6.0)).Size(1.5, 1.5);
        cell_1_3 = maze.Area('cell-1-3').At(Position(1.5, 4.5)).Size(1.5, 1.5);
        cell_2_3 = maze.Area('cell-2-3').At(Position(3.0, 4.5)).Size(1.5, 1.5);
        cell_3_3 = maze.Area('cell-3-3').At(Position(4.5, 4.5)).Size(1.5, 1.5);
        cell_4_3 = maze.Area('cell-4-3').At(Position(6.0, 4.5)).Size(1.5, 1.5);
        cell_6_3 = maze.Area('cell-6-3').At(Position(9.0, 4.5)).Size(1.5, 1.5);
        cell_7_3 = maze.Area('cell-7-3').At(Position(10.5, 4.5)).Size(1.5, 1.5);
        cell_8_3 = maze.Area('cell-8-3').At(Position(12.0, 4.5)).Size(1.5, 1.5);
        cell_10_3 = maze.Area('cell-10-3').At(Position(15.0, 4.5)).Size(1.5, 1.5);
        cell_11_3 = maze.Area('cell-11-3').At(Position(16.5, 4.5)).Size(1.5, 1.5);
        cell_12_3 = maze.Area('cell-12-3').At(Position(18.0, 4.5)).Size(1.5, 1.5);
        cell_14_3 = maze.Area('cell-14-3').At(Position(21.0, 4.5)).Size(1.5, 1.5);
        cell_15_3 = maze.Area('cell-15-3').At(Position(22.5, 4.5)).Size(1.5, 1.5);
        cell_16_3 = maze.Area('cell-16-3').At(Position(24.0, 4.5)).Size(1.5, 1.5);
        cell_17_3 = maze.Area('cell-17-3').At(Position(25.5, 4.5)).Size(1.5, 1.5);
        cell_1_2 = maze.Area('cell-1-2').At(Position(1.5, 3.0)).Size(1.5, 1.5);
        cell_8_2 = maze.Area('cell-8-2').At(Position(12.0, 3.0)).Size(1.5, 1.5);
        cell_10_2 = maze.Area('cell-10-2').At(Position(15.0, 3.0)).Size(1.5, 1.5);
        cell_17_2 = maze.Area('cell-17-2').At(Position(25.5, 3.0)).Size(1.5, 1.5);
        cell_1_1 = maze.Area('cell-1-1').At(Position(1.5, 1.5)).Size(1.5, 1.5);
        cell_2_1 = maze.Area('cell-2-1').At(Position(3.0, 1.5)).Size(1.5, 1.5);
        cell_3_1 = maze.Area('cell-3-1').At(Position(4.5, 1.5)).Size(1.5, 1.5);
        cell_4_1 = maze.Area('cell-4-1').At(Position(6.0, 1.5)).Size(1.5, 1.5);
        cell_5_1 = maze.Area('cell-5-1').At(Position(7.5, 1.5)).Size(1.5, 1.5);
        cell_6_1 = maze.Area('cell-6-1').At(Position(9.0, 1.5)).Size(1.5, 1.5);
        cell_7_1 = maze.Area('cell-7-1').At(Position(10.5, 1.5)).Size(1.5, 1.5);
        cell_8_1 = maze.Area('cell-8-1').At(Position(12.0, 1.5)).Size(1.5, 1.5);
        cell_9_1 = maze.Area('cell-9-1').At(Position(13.5, 1.5)).Size(1.5, 1.5);
        cell_10_1 = maze.Area('cell-10-1').At(Position(15.0, 1.5)).Size(1.5, 1.5);
        cell_11_1 = maze.Area('cell-11-1').At(Position(16.5, 1.5)).Size(1.5, 1.5);
        cell_12_1 = maze.Area('cell-12-1').At(Position(18.0, 1.5)).Size(1.5, 1.5);
        cell_13_1 = maze.Area('cell-13-1').At(Position(19.5, 1.5)).Size(1.5, 1.5);
        cell_14_1 = maze.Area('cell-14-1').At(Position(21.0, 1.5)).Size(1.5, 1.5);
        cell_15_1 = maze.Area('cell-15-1').At(Position(22.5, 1.5)).Size(1.5, 1.5);
        cell_16_1 = maze.Area('cell-16-1').At(Position(24.0, 1.5)).Size(1.5, 1.5);
        cell_17_1 = maze.Area('cell-17-1').At(Position(25.5, 1.5)).Size(1.5, 1.5);
        cell_1_19.OpenTo(cell_2_19);
        cell_2_19.OpenTo(cell_3_19);
        cell_3_19.OpenTo(cell_4_19);
        cell_4_19.OpenTo(cell_5_19);
        cell_5_19.OpenTo(cell_6_19);
        cell_6_19.OpenTo(cell_7_19);
        cell_7_19.OpenTo(cell_8_19);
        cell_10_19.OpenTo(cell_11_19);
        cell_11_19.OpenTo(cell_12_19);
        cell_12_19.OpenTo(cell_13_19);
        cell_13_19.OpenTo(cell_14_19);
        cell_14_19.OpenTo(cell_15_19);
        cell_15_19.OpenTo(cell_16_19);
        cell_16_19.OpenTo(cell_17_19);
        cell_1_18.OpenTo(cell_1_19);
        cell_4_18.OpenTo(cell_4_19);
        cell_8_18.OpenTo(cell_8_19);
        cell_10_18.OpenTo(cell_10_19);
        cell_14_18.OpenTo(cell_14_19);
        cell_17_18.OpenTo(cell_17_19);
        cell_1_17.OpenTo(cell_2_17);
        cell_1_17.OpenTo(cell_1_18);
        cell_2_17.OpenTo(cell_3_17);
        cell_3_17.OpenTo(cell_4_17);
        cell_4_17.OpenTo(cell_5_17);
        cell_4_17.OpenTo(cell_4_18);
        cell_5_17.OpenTo(cell_6_17);
        cell_6_17.OpenTo(cell_7_17);
        cell_7_17.OpenTo(cell_8_17);
        cell_8_17.OpenTo(cell_9_17);
        cell_8_17.OpenTo(cell_8_18);
        cell_9_17.OpenTo(cell_10_17);
        cell_10_17.OpenTo(cell_11_17);
        cell_10_17.OpenTo(cell_10_18);
        cell_11_17.OpenTo(cell_12_17);
        cell_12_17.OpenTo(cell_13_17);
        cell_13_17.OpenTo(cell_14_17);
        cell_14_17.OpenTo(cell_15_17);
        cell_14_17.OpenTo(cell_14_18);
        cell_15_17.OpenTo(cell_16_17);
        cell_16_17.OpenTo(cell_17_17);
        cell_17_17.OpenTo(cell_17_18);
        cell_1_16.OpenTo(cell_1_17);
        cell_4_16.OpenTo(cell_4_17);
        cell_6_16.OpenTo(cell_6_17);
        cell_12_16.OpenTo(cell_12_17);
        cell_14_16.OpenTo(cell_14_17);
        cell_17_16.OpenTo(cell_17_17);
        cell_1_15.OpenTo(cell_2_15);
        cell_1_15.OpenTo(cell_1_16);
        cell_2_15.OpenTo(cell_3_15);
        cell_3_15.OpenTo(cell_4_15);
        cell_4_15.OpenTo(cell_4_16);
        cell_6_15.OpenTo(cell_7_15);
        cell_6_15.OpenTo(cell_6_16);
        cell_7_15.OpenTo(cell_8_15);
        cell_10_15.OpenTo(cell_11_15);
        cell_11_15.OpenTo(cell_12_15);
        cell_12_15.OpenTo(cell_12_16);
        cell_14_15.OpenTo(cell_15_15);
        cell_14_15.OpenTo(cell_14_16);
        cell_15_15.OpenTo(cell_16_15);
        cell_16_15.OpenTo(cell_17_15);
        cell_17_15.OpenTo(cell_17_16);
        cell_4_14.OpenTo(cell_4_15);
        cell_8_14.OpenTo(cell_8_15);
        cell_10_14.OpenTo(cell_10_15);
        cell_14_14.OpenTo(cell_14_15);
        cell_1_13.OpenTo(cell_2_13);
        cell_2_13.OpenTo(cell_3_13);
        cell_3_13.OpenTo(cell_4_13);
        cell_4_13.OpenTo(cell_5_13);
        cell_4_13.OpenTo(cell_4_14);
        cell_5_13.OpenTo(cell_6_13);
        cell_8_13.OpenTo(cell_8_14);
        cell_10_13.OpenTo(cell_10_14);
        cell_12_13.OpenTo(cell_13_13);
        cell_13_13.OpenTo(cell_14_13);
        cell_14_13.OpenTo(cell_15_13);
        cell_14_13.OpenTo(cell_14_14);
        cell_15_13.OpenTo(cell_16_13);
        cell_16_13.OpenTo(cell_17_13);
        cell_1_12.OpenTo(cell_1_13);
        cell_6_12.OpenTo(cell_6_13);
        cell_8_12.OpenTo(cell_9_12);
        cell_8_12.OpenTo(cell_8_13);
        cell_9_12.OpenTo(cell_10_12);
        cell_10_12.OpenTo(cell_10_13);
        cell_12_12.OpenTo(cell_12_13);
        cell_17_12.OpenTo(cell_17_13);
        cell_1_11.OpenTo(cell_2_11);
        cell_1_11.OpenTo(cell_1_12);
        cell_2_11.OpenTo(cell_3_11);
        cell_3_11.OpenTo(cell_4_11);
        cell_4_11.OpenTo(cell_5_11);
        cell_5_11.OpenTo(cell_6_11);
        cell_6_11.OpenTo(cell_6_12);
        cell_8_11.OpenTo(cell_9_11);
        cell_8_11.OpenTo(cell_8_12);
        cell_9_11.OpenTo(cell_10_11);
        cell_9_11.OpenTo(cell_9_12);
        cell_10_11.OpenTo(cell_10_12);
        cell_12_11.OpenTo(cell_13_11);
        cell_12_11.OpenTo(cell_12_12);
        cell_13_11.OpenTo(cell_14_11);
        cell_14_11.OpenTo(cell_15_11);
        cell_15_11.OpenTo(cell_16_11);
        cell_16_11.OpenTo(cell_17_11);
        cell_17_11.OpenTo(cell_17_12);
        cell_1_10.OpenTo(cell_1_11);
        cell_6_10.OpenTo(cell_6_11);
        cell_12_10.OpenTo(cell_12_11);
        cell_17_10.OpenTo(cell_17_11);
        cell_1_9.OpenTo(cell_2_9);
        cell_1_9.OpenTo(cell_1_10);
        cell_2_9.OpenTo(cell_3_9);
        cell_3_9.OpenTo(cell_4_9);
        cell_4_9.OpenTo(cell_5_9);
        cell_5_9.OpenTo(cell_6_9);
        cell_6_9.OpenTo(cell_7_9);
        cell_6_9.OpenTo(cell_6_10);
        cell_7_9.OpenTo(cell_8_9);
        cell_10_9.OpenTo(cell_11_9);
        cell_11_9.OpenTo(cell_12_9);
        cell_12_9.OpenTo(cell_13_9);
        cell_12_9.OpenTo(cell_12_10);
        cell_13_9.OpenTo(cell_14_9);
        cell_14_9.OpenTo(cell_15_9);
        cell_15_9.OpenTo(cell_16_9);
        cell_16_9.OpenTo(cell_17_9);
        cell_17_9.OpenTo(cell_17_10);
        cell_4_8.OpenTo(cell_4_9);
        cell_8_8.OpenTo(cell_8_9);
        cell_10_8.OpenTo(cell_10_9);
        cell_14_8.OpenTo(cell_14_9);
        cell_1_7.OpenTo(cell_2_7);
        cell_2_7.OpenTo(cell_3_7);
        cell_3_7.OpenTo(cell_4_7);
        cell_4_7.OpenTo(cell_5_7);
        cell_4_7.OpenTo(cell_4_8);
        cell_5_7.OpenTo(cell_6_7);
        cell_6_7.OpenTo(cell_7_7);
        cell_7_7.OpenTo(cell_8_7);
        cell_8_7.OpenTo(cell_8_8);
        cell_10_7.OpenTo(cell_11_7);
        cell_10_7.OpenTo(cell_10_8);
        cell_11_7.OpenTo(cell_12_7);
        cell_12_7.OpenTo(cell_13_7);
        cell_13_7.OpenTo(cell_14_7);
        cell_14_7.OpenTo(cell_15_7);
        cell_14_7.OpenTo(cell_14_8);
        cell_15_7.OpenTo(cell_16_7);
        cell_16_7.OpenTo(cell_17_7);
        cell_1_6.OpenTo(cell_1_7);
        cell_4_6.OpenTo(cell_4_7);
        cell_8_6.OpenTo(cell_8_7);
        cell_10_6.OpenTo(cell_10_7);
        cell_14_6.OpenTo(cell_14_7);
        cell_17_6.OpenTo(cell_17_7);
        cell_1_5.OpenTo(cell_2_5);
        cell_1_5.OpenTo(cell_1_6);
        cell_4_5.OpenTo(cell_5_5);
        cell_4_5.OpenTo(cell_4_6);
        cell_5_5.OpenTo(cell_6_5);
        cell_6_5.OpenTo(cell_7_5);
        cell_7_5.OpenTo(cell_8_5);
        cell_8_5.OpenTo(cell_9_5);
        cell_8_5.OpenTo(cell_8_6);
        cell_9_5.OpenTo(cell_10_5);
        cell_10_5.OpenTo(cell_11_5);
        cell_10_5.OpenTo(cell_10_6);
        cell_11_5.OpenTo(cell_12_5);
        cell_12_5.OpenTo(cell_13_5);
        cell_13_5.OpenTo(cell_14_5);
        cell_14_5.OpenTo(cell_14_6);
        cell_16_5.OpenTo(cell_17_5);
        cell_17_5.OpenTo(cell_17_6);
        cell_2_4.OpenTo(cell_2_5);
        cell_4_4.OpenTo(cell_4_5);
        cell_6_4.OpenTo(cell_6_5);
        cell_12_4.OpenTo(cell_12_5);
        cell_14_4.OpenTo(cell_14_5);
        cell_16_4.OpenTo(cell_16_5);
        cell_1_3.OpenTo(cell_2_3);
        cell_2_3.OpenTo(cell_3_3);
        cell_2_3.OpenTo(cell_2_4);
        cell_3_3.OpenTo(cell_4_3);
        cell_4_3.OpenTo(cell_4_4);
        cell_6_3.OpenTo(cell_7_3);
        cell_6_3.OpenTo(cell_6_4);
        cell_7_3.OpenTo(cell_8_3);
        cell_10_3.OpenTo(cell_11_3);
        cell_11_3.OpenTo(cell_12_3);
        cell_12_3.OpenTo(cell_12_4);
        cell_14_3.OpenTo(cell_15_3);
        cell_14_3.OpenTo(cell_14_4);
        cell_15_3.OpenTo(cell_16_3);
        cell_16_3.OpenTo(cell_17_3);
        cell_16_3.OpenTo(cell_16_4);
        cell_1_2.OpenTo(cell_1_3);
        cell_8_2.OpenTo(cell_8_3);
        cell_10_2.OpenTo(cell_10_3);
        cell_17_2.OpenTo(cell_17_3);
        cell_1_1.OpenTo(cell_2_1);
        cell_1_1.OpenTo(cell_1_2);
        cell_2_1.OpenTo(cell_3_1);
        cell_3_1.OpenTo(cell_4_1);
        cell_4_1.OpenTo(cell_5_1);
        cell_5_1.OpenTo(cell_6_1);
        cell_6_1.OpenTo(cell_7_1);
        cell_7_1.OpenTo(cell_8_1);
        cell_8_1.OpenTo(cell_9_1);
        cell_8_1.OpenTo(cell_8_2);
        cell_9_1.OpenTo(cell_10_1);
        cell_10_1.OpenTo(cell_11_1);
        cell_10_1.OpenTo(cell_10_2);
        cell_11_1.OpenTo(cell_12_1);
        cell_12_1.OpenTo(cell_13_1);
        cell_13_1.OpenTo(cell_14_1);
        cell_14_1.OpenTo(cell_15_1);
        cell_15_1.OpenTo(cell_16_1);
        cell_16_1.OpenTo(cell_17_1);
        cell_17_1.OpenTo(cell_17_2);
    }
    mazeScenario = g.Stage(maze);
}
";

    // True only on the boot where the journal was brand-new (the framework raises
    // OnFirstHydration exactly then). Used for the panel's birth announcement.
    internal bool BornThisBoot { get; private set; }

    private readonly string golemName;   // who the golem is (env GOLEM) — handed to the init release as a parameter (ajuste 72)
    private readonly IReadOnlyList<string> cast;   // whom the golem addresses (its peers and whom it tells its stops): the 3.0 engine's cast

    internal GolemPerformance(string actorName, string golemName, IReadOnlyList<string> cast, params Assembly[] libraryAssemblies)
        : base(actorName, libraryAssemblies)
    {
        if (string.IsNullOrWhiteSpace(golemName)) throw new ArgumentException("a golem needs a name to be born with", nameof(golemName));
        this.golemName = golemName;
        this.cast = cast ?? Array.Empty<string>();
        foreach (var name in this.cast)
            if (string.IsNullOrEmpty(name) || !name.All(c => char.IsLetterOrDigit(c) || c == '_') || char.IsDigit(name[0]))
                throw new ArgumentException($"'{name}' cannot be declared as an addressee: a role is an identifier", nameof(cast));
    }

    protected override void OnFirstHydration()
    {
        BornThisBoot = true;
    }

    // The operator wants to see EVERYTHING the journal receives — defines, actions,
    // literal scripts, tells, acks, verdicts — so the panel taps the framework's
    // StageHook: every record as it is written (live), and the whole journal at boot.
    internal void WatchJournal(Action<long, byte[]> onRecordWritten)
    {
        hook.OnRecordWritten = (entryId, wire) => onRecordWritten(entryId, wire);
    }

    internal List<Puppeteer.EventSourcing.DB.JournalWireRecord> ReadJournalAfter(long afterEntryId)
    {
        var records = new List<Puppeteer.EventSourcing.DB.JournalWireRecord>();
        hook.ReadJournalRecordsAfter(afterEntryId, records);
        return records;
    }

    // The release chain.
    //   init    — the golem is born with its body: its size (the radius of the disk it occupies,
    //             matching the chassis the sim builds — with it the golem reckons WHERE a touch
    //             happened and holds that point against its map), its cruise speed (world units
    //             per second) and how long it lingers at a stop a peer told it about.
    //   map_v1  — the golem charts its map: a ring of rooms around two solid blocks, with a
    //             wide shortcut through the middle (north ~ center ~ south, open boundaries)
    //             and two long narrow corridors on the sides (west, east). One fluent chain
    //             per place declaring its doors (a point on the shared wall) and its open
    //             boundaries. Shortest roads become visible: kitchen -> garage cuts through
    //             the center, kitchen -> living takes the west corridor. The map is the golem's
    //             own knowledge: roads are planned through the passages (shortest path), and
    //             every golem shares the same map.
    // (7-sep-2026: the language was rewritten in one go — MoveTo/Cover/Follow, Cross/Reach,
    // Fail/Abandon, Chart/DoorTo/OpenTo, Embody/Cruise/Linger — and the journals of the
    // turtlesim era were archived; journals born before that day do not rehydrate.)
    // (10-sep-2026: the modules became globals of the actor — body, map, layout, collisions —
    // built in their own releases and handed to the golem: g = Golem(body, layout, collisions).
    // Stops, orders and roads are objects (Position, the map's passages); the road is written
    // act by act (Route, Via, Around, Aside, Stop). Journals born before that day were archived
    // as journal-legacy-20260910-*; they do not rehydrate.)
    // (29-sep-2026, ajuste 60: the golem is born with its body alone and ENTERS a scenario — a map with its own
    // collisions. The golem is born right after its body (`init`, second); every map release names its map's own variable
    // (`warehouse`, `openFloor`) and ends with the golem STAGING it (`warehouseScenario = g.Stage(warehouse)`, its collisions
    // born empty inside) — the warehouse's release enters it too, the golem's home scenario. There is NO global `map` or
    // `collisions` any more: every script reads the scenario the golem is in — `g.Current.Map`, `g.Current.Collisions`
    // (Juan: "no serían necesarios… ya quedaron seteados al momento de g.Enter"). Journals before that day were archived
    // as journal-legacy-20260929-scenarios/ and -variables/; they do not rehydrate.)
    // (30-sep-2026, ajuste 72: the golem is born with its NAME too — `g = Golem(body, @golemName)`, the name from GOLEM as a
    // parameter of the release; the init's body changed, so journals before that day were archived as journal-legacy-20260930-name/.)
    // RULE: an applied release is never edited (the engine guards its body signature) —
    // evolve the golem by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('body_v1') {
                radius = Meters(0.25);
                speed = MetersPerSecond(2.0);
                linger = Seconds(6.0);
                retreat = Meters(0.6);
                body = Body(radius, speed, linger, retreat);
            }
            upgrade('init') {
                g = Golem(body, @golemName);
            }
            upgrade('warehouse_v1') {
                warehouse = MapLayout('warehouse');
                {
                    kitchen = warehouse.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0);
                    north = warehouse.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0);
                    storage = warehouse.Area('storage').At(Position(7.0, 8.0)).Size(4.0, 3.0);
                    west = warehouse.Area('west').At(Position(0.0, 3.0)).Size(1.5, 5.0);
                    center = warehouse.Area('center').At(Position(4.0, 3.0)).Size(3.0, 5.0);
                    east = warehouse.Area('east').At(Position(9.5, 3.0)).Size(1.5, 5.0);
                    living = warehouse.Area('living').At(Position(0.0, 0.0)).Size(4.0, 3.0);
                    south = warehouse.Area('south').At(Position(4.0, 0.0)).Size(3.0, 3.0);
                    garage = warehouse.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);
                    kitchen.DoorAt(north, Position(4.0, 9.5));
                    kitchen.DoorAt(west, Position(0.75, 8.0));
                    north.DoorAt(storage, Position(7.0, 9.5));
                    north.OpenTo(center);
                    storage.DoorAt(east, Position(10.25, 8.0));
                    west.DoorAt(living, Position(0.75, 3.0));
                    center.OpenTo(south);
                    east.DoorAt(garage, Position(10.25, 3.0));
                    living.DoorAt(south, Position(4.0, 1.5));
                    south.DoorAt(garage, Position(7.0, 1.5));
                }
                warehouseScenario = g.Stage(warehouse);
                g.Enter(warehouseScenario);
            }
            upgrade('open-floor_v1') {
                openFloor = MapLayout('open-floor');
                {
                    floor = openFloor.Area('floor').At(Position(0.0, 0.0)).Size(11.0, 11.0);
                }
                openFloorScenario = g.Stage(openFloor);
            }
        " + MazeRelease + @"
        ")
        .WithParameters(p => { p["golemName", typeof(string)] = golemName; })   // the same release for every golem; only who it is changes
        .PerformCommand();

        // THE CAST (motor 3.0, 21-oct-2026; the engine's reactions guide, "An actor declares whom it addresses"): since d040150/2a0b001 an
        // utterance to a name the actor never declared is refused — "This actor tells 'blue', which it never declared it addresses" — and the
        // declaration STATES the whole cast in one sentence, inside a versioned upgrade, so replay rebuilds it. The names are the golem's peers
        // and whom it tells its stops (TELL_ROUTES / TELL_DONE_TO): a role is an identifier, never a parameter, so the sentence is built from
        // them here — the one release whose text differs per golem. A golem with nobody to address declares nothing (`tells to ;` would be
        // the empty cast). A fleet that grows needs the next release (`cast_v2`): an applied release is never edited.
        if (cast.Count > 0)
        {
            Actor.Using("upgrade('cast_v1') { tells to " + string.Join(", ", cast) + "; }").PerformCommand();
        }
    }
}

package vn.networkadmin.client.infrastructure.monitoring;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

class NetworkTrafficRateCalculatorTest {
    private static final double EPSILON = 1e-9;

    @Test
    void firstSampleOnlyEstablishesBaseline() {
        var calculator = new NetworkTrafficRateCalculator();

        var rate = calculator.sample(1_000L, 2_000L, 1_000_000_000L);

        assertEquals(0, rate.downloadMbps(), EPSILON);
        assertEquals(0, rate.uploadMbps(), EPSILON);
    }

    @Test
    void calculatesDownloadAndUploadFromCounterDeltas() {
        var calculator = new NetworkTrafficRateCalculator();
        calculator.sample(0L, 0L, 1_000_000_000L);

        var rate = calculator.sample(1_000_000L, 250_000L, 2_000_000_000L);

        assertEquals(8.0, rate.downloadMbps(), EPSILON);
        assertEquals(2.0, rate.uploadMbps(), EPSILON);
    }

    @Test
    void convertsBytesToMegabitsUsingDecimalMegabits() {
        var calculator = new NetworkTrafficRateCalculator();
        calculator.sample(0L, 0L, 0L);

        var rate = calculator.sample(125_000L, 0L, 1_000_000_000L);

        assertEquals(1.0, rate.downloadMbps(), EPSILON);
    }

    @Test
    void counterResetProducesZeroThenUsesNewBaseline() {
        var calculator = new NetworkTrafficRateCalculator();
        calculator.sample(10_000L, 20_000L, 1_000_000_000L);

        var resetRate = calculator.sample(500L, 700L, 2_000_000_000L);
        var nextRate = calculator.sample(1_500L, 1_700L, 3_000_000_000L);

        assertEquals(0, resetRate.downloadMbps(), EPSILON);
        assertEquals(0, resetRate.uploadMbps(), EPSILON);
        assertEquals(0.008, nextRate.downloadMbps(), EPSILON);
        assertEquals(0.008, nextRate.uploadMbps(), EPSILON);
    }

    @Test
    void invalidOrUnavailableCountersDoNotCrashOrPoisonBaseline() {
        var calculator = new NetworkTrafficRateCalculator();

        assertEquals(NetworkTrafficRateCalculator.Rate.ZERO,
                calculator.sample(null, null, 1_000_000_000L));
        assertEquals(NetworkTrafficRateCalculator.Rate.ZERO,
                calculator.sample(-1L, 2L, 2_000_000_000L));
        assertEquals(0, calculator.sample(100L, 200L, 3_000_000_000L).downloadMbps(), EPSILON);

        var rate = calculator.sample(1_100L, 1_200L, 4_000_000_000L);
        assertEquals(0.008, rate.downloadMbps(), EPSILON);
        assertEquals(0.008, rate.uploadMbps(), EPSILON);
    }

    @Test
    void handlesZeroTrafficAndDifferentSamplingIntervals() {
        var calculator = new NetworkTrafficRateCalculator();
        calculator.sample(5_000L, 8_000L, 1_000_000_000L);

        var zero = calculator.sample(5_000L, 8_000L, 3_000_000_000L);
        var twoSecondRate = calculator.sample(505_000L, 20_500L, 5_000_000_000L);

        assertEquals(0, zero.downloadMbps(), EPSILON);
        assertEquals(0, zero.uploadMbps(), EPSILON);
        assertEquals(2.0, twoSecondRate.downloadMbps(), EPSILON);
        assertEquals(0.05, twoSecondRate.uploadMbps(), EPSILON);
    }

    @Test
    void nonIncreasingClockReestablishesBaseline() {
        var calculator = new NetworkTrafficRateCalculator();
        calculator.sample(1_000L, 1_000L, 2_000_000_000L);

        var rate = calculator.sample(2_000L, 2_000L, 1_000_000_000L);

        assertEquals(0, rate.downloadMbps(), EPSILON);
        assertEquals(0, rate.uploadMbps(), EPSILON);
    }

    @Test
    void rollingHistoryKeepsOnlyTheLatestSixtySamples() {
        var history = new NetworkTrafficHistory(60);
        for (int i = 0; i < 61; i++) history.add(i, i * 2.0);

        assertEquals(60, history.size());
        assertEquals(1.0, history.snapshot().get(0).downloadMbps(), EPSILON);
        assertEquals(60.0, history.snapshot().get(59).downloadMbps(), EPSILON);
    }

    @Test
    void historyNormalizesMissingAndNegativeValues() {
        var history = new NetworkTrafficHistory(2);
        history.add(Double.NaN, -4);
        history.add(Double.POSITIVE_INFINITY, 3);

        assertTrue(history.snapshot().stream().allMatch(sample ->
                sample.downloadMbps() >= 0 && sample.uploadMbps() >= 0));
        assertEquals(0, history.snapshot().get(0).downloadMbps(), EPSILON);
        assertEquals(3, history.snapshot().get(1).uploadMbps(), EPSILON);
    }
}

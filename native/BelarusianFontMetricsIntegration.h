#ifndef GTA_GXT_BELARUSIAN_FONT_METRICS_INTEGRATION_H
#define GTA_GXT_BELARUSIAN_FONT_METRICS_INTEGRATION_H

/*
 * Native integration adapter for the maintained BelarusianLanguage ASI source.
 * The ASI must include this file instead of declaring a second metrics table.
 */
#include "generated/BelarusianFontMetrics.generated.h"

#define BELARUSIAN_FONT_METRICS_INTEGRATION_CONTRACT_VERSION 1u

/* C89-compatible compile-time guards for the physical Vice City row contract. */
typedef char BelarusianFontMetrics_RequiresTwoRows[
    (BELARUSIAN_FONT_METRIC_ROW_COUNT == 2u) ? 1 : -1];
typedef char BelarusianFontMetrics_RequiresFont2AtRowZero[
    (BELARUSIAN_FONT_ROW_FONT2 == 0) ? 1 : -1];
typedef char BelarusianFontMetrics_RequiresFont1AtRowOne[
    (BELARUSIAN_FONT_ROW_FONT1 == 1) ? 1 : -1];

/*
 * Copies the canonical base rows into Vice City's Size[2][210] destination.
 * Physical order is always row 0/font2 followed by row 1/font1.
 */
static void BelarusianFontMetrics_InstallBaseRows(
    volatile uint16_t destination[BELARUSIAN_FONT_METRIC_ROW_COUNT][BELARUSIAN_FONT_METRIC_COUNT])
{
    uint32_t row;
    uint32_t index;

    for (row = 0u; row < BELARUSIAN_FONT_METRIC_ROW_COUNT; ++row) {
        for (index = 0u; index < BELARUSIAN_FONT_METRIC_COUNT; ++index) {
            destination[row][index] = kBelarusianFontMetrics[row][index];
        }
    }
}

/*
 * Looks up the declarative sparse runtime override for a context/font/code key.
 * Returns 1 and writes result when found; returns 0 when the base row applies.
 * Heading T/t entries represent effective preview compatibility widths only;
 * maintained ASI code must preserve Vice City's heading remap to metric index 198.
 */
static int BelarusianFontMetrics_TryGetOverride(
    BelarusianFontRenderContext context,
    BelarusianFontMetricRow font_row,
    uint8_t code,
    uint16_t *result)
{
    uint32_t index;
    const BelarusianFontMetricOverride *item;

    if (result == 0) {
        return 0;
    }

    for (index = 0u; index < BELARUSIAN_FONT_METRIC_OVERRIDE_COUNT; ++index) {
        item = &kBelarusianFontMetricOverrides[index];
        if (item->context == (uint8_t)context &&
            item->font_row == (uint8_t)font_row &&
            item->code == code) {
            *result = item->advance;
            return 1;
        }
    }

    return 0;
}

#endif /* GTA_GXT_BELARUSIAN_FONT_METRICS_INTEGRATION_H */

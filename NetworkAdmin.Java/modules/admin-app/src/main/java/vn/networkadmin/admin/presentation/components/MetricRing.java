package vn.networkadmin.admin.presentation.components;

import vn.networkadmin.admin.presentation.theme.AdminTheme;

import javax.swing.*;
import java.awt.*;

public final class MetricRing extends JComponent {
    private double value;
    private final Color color;
    private final String caption;
    private final DashboardIcon.Kind iconKind;
    private boolean available = true;

    public MetricRing(String caption, Color color, DashboardIcon.Kind iconKind) {
        this.caption = caption;
        this.color = color;
        this.iconKind = iconKind;
        setPreferredSize(new Dimension(130, 125));
        setMinimumSize(new Dimension(100, 115));
        setOpaque(false);
    }

    public void setValue(double value) {
        this.value = Math.max(0, Math.min(100, value));
        this.available = true;
        repaint();
    }

    public void setUnavailable() {
        this.available = false;
        repaint();
    }

    @Override
    protected void paintComponent(Graphics graphics) {
        super.paintComponent(graphics);
        Graphics2D g = (Graphics2D) graphics.create();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);

        int diameter = 74;
        int x = (getWidth() - diameter) / 2;
        int y = 4;

        // Background track
        g.setStroke(new BasicStroke(7.5f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.setColor(new Color(226, 232, 240));
        g.drawArc(x, y, diameter, diameter, 90, -360);

        // Progress arc
        if (available && value > 0) {
            g.setColor(color);
            int angle = (int) Math.round(-360.0 * value / 100.0);
            g.drawArc(x, y, diameter, diameter, 90, angle);
        }

        // Percentage text inside circle
        String text = available ? String.format(java.util.Locale.ROOT, "%.0f%%", value) : "--";
        g.setFont(new Font("Segoe UI", Font.BOLD, 18));
        g.setColor(AdminTheme.TEXT);
        FontMetrics fm = g.getFontMetrics();
        int textX = (getWidth() - fm.stringWidth(text)) / 2;
        int textY = y + diameter / 2 + fm.getAscent() / 2 - 2;
        g.drawString(text, textX, textY);

        // Bottom label: icon + caption
        int iconSize = 15;
        DashboardIcon icon = new DashboardIcon(iconKind, AdminTheme.TEXT, iconSize);
        g.setFont(new Font("Segoe UI", Font.BOLD, 12));
        FontMetrics fmLabel = g.getFontMetrics();
        int labelWidth = iconSize + 6 + fmLabel.stringWidth(caption);
        int startX = (getWidth() - labelWidth) / 2;
        int bottomY = y + diameter + 24;

        icon.paintIcon(this, g, startX, bottomY - iconSize + 2);
        g.setColor(AdminTheme.TEXT);
        g.drawString(caption, startX + iconSize + 6, bottomY);

        g.dispose();
    }
}

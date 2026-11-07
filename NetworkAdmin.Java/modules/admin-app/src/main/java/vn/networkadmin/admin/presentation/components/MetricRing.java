package vn.networkadmin.admin.presentation.components;

import vn.networkadmin.admin.presentation.theme.AdminTheme;

import javax.swing.*;
import java.awt.*;

public final class MetricRing extends JComponent {
    private double value;
    private Color color;
    private final String caption;

    public MetricRing(String caption, Color color) {
        this.caption = caption; this.color = color;
        setPreferredSize(new Dimension(150, 150)); setMinimumSize(new Dimension(110, 120));
        setOpaque(false);
    }
    public void setValue(double value) { this.value = Math.max(0, Math.min(100, value)); repaint(); }

    @Override protected void paintComponent(Graphics graphics) {
        super.paintComponent(graphics);
        Graphics2D g = (Graphics2D) graphics.create();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        int diameter = Math.min(getWidth() - 28, getHeight() - 46);
        diameter = Math.max(54, diameter);
        int x = (getWidth() - diameter) / 2;
        int y = 4;
        g.setStroke(new BasicStroke(7, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        g.setColor(AdminTheme.BORDER); g.drawArc(x, y, diameter, diameter, 90, -360);
        g.setColor(color); g.drawArc(x, y, diameter, diameter, 90, (int) (-360 * value / 100));
        String text = String.format(java.util.Locale.ROOT, "%.1f%%", value);
        g.setFont(new Font("Segoe UI", Font.BOLD, Math.max(16, diameter / 6)));
        g.setColor(AdminTheme.TEXT);
        FontMetrics fm = g.getFontMetrics();
        g.drawString(text, (getWidth() - fm.stringWidth(text)) / 2, y + diameter / 2 + fm.getAscent() / 3);
        g.setFont(AdminTheme.REGULAR); g.setColor(AdminTheme.MUTED); fm = g.getFontMetrics();
        g.drawString(caption, (getWidth() - fm.stringWidth(caption)) / 2, getHeight() - 8);
        g.dispose();
    }
}

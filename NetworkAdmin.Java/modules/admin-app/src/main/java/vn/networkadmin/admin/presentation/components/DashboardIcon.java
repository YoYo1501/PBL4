package vn.networkadmin.admin.presentation.components;

import javax.swing.*;
import java.awt.*;
import java.awt.geom.*;
import java.awt.image.BufferedImage;

/** Crisp vector icons matching the modern admin dashboard design. */
public final class DashboardIcon implements Icon {
    public enum Kind {
        GLOBE, DASHBOARD, CLIENTS, SCAN, PING, MONITORING, SETTINGS, ABOUT,
        SCREEN, POWER, CHAT, CLOCK, PEOPLE, REFRESH, DISCONNECT,
        WINDOWS, ID_CARD, CALENDAR, WIFI, CPU, RAM, LAPTOP,
        CHECK_CIRCLE, X_CIRCLE, LAYERS, JAVA, CODE, PLAY, CROSS,
        SERVER, SHIELD, CERTIFICATE, KEY, LOCK, COPY, EYE_OFF, RADIO,
        EMPTY_SCAN, EMPTY_PING
    }

    private final Kind kind;
    private final Color color;
    private final int size;

    public DashboardIcon(Kind kind, Color color, int size) {
        this.kind = kind;
        this.color = color;
        this.size = size;
    }

    @Override public int getIconWidth() { return size; }
    @Override public int getIconHeight() { return size; }

    public static Image createGlobeImage(int size, Color color) {
        BufferedImage img = new BufferedImage(size, size, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = img.createGraphics();
        DashboardIcon icon = new DashboardIcon(Kind.GLOBE, color, size);
        icon.paintIcon(null, g, 0, 0);
        g.dispose();
        return img;
    }

    @Override
    public void paintIcon(Component component, Graphics graphics, int x, int y) {
        Graphics2D g = (Graphics2D) graphics.create();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_STROKE_CONTROL, RenderingHints.VALUE_STROKE_PURE);
        g.translate(x, y);
        double scale = size / 36.0;
        g.scale(scale, scale);
        g.setColor(color);
        g.setStroke(new BasicStroke(2.7f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));

        switch (kind) {
            case GLOBE -> paintGlobe(g);
            case DASHBOARD -> paintDashboard(g);
            case CLIENTS, PEOPLE -> paintClients(g);
            case SCAN -> paintScan(g);
            case PING -> paintPing(g);
            case MONITORING -> paintMonitoring(g);
            case SCREEN -> paintScreen(g);
            case POWER -> paintPower(g);
            case CHAT -> paintChat(g);
            case SETTINGS -> paintSettings(g);
            case ABOUT -> paintAbout(g);
            case CLOCK -> paintClock(g);
            case REFRESH -> paintRefresh(g);
            case DISCONNECT -> paintDisconnect(g);
            case WINDOWS -> paintWindows(g);
            case ID_CARD -> paintIdCard(g);
            case CALENDAR -> paintCalendar(g);
            case WIFI -> paintWifi(g);
            case CPU -> paintCpu(g);
            case RAM -> paintRam(g);
            case LAPTOP -> paintLaptop(g);
            case CHECK_CIRCLE -> paintCheckCircle(g);
            case X_CIRCLE -> paintXCircle(g);
            case LAYERS -> paintLayers(g);
            case JAVA -> paintJava(g);
            case CODE -> paintCode(g);
            case PLAY -> paintPlay(g);
            case CROSS -> paintCross(g);
            case SERVER -> paintServer(g);
            case SHIELD -> paintShield(g);
            case CERTIFICATE -> paintCertificate(g);
            case KEY -> paintKey(g);
            case LOCK -> paintLock(g);
            case COPY -> paintCopy(g);
            case EYE_OFF -> paintEyeOff(g);
            case RADIO -> paintRadio(g);
            case EMPTY_SCAN -> paintEmptyScan(g);
            case EMPTY_PING -> paintEmptyPing(g);
        }
        g.dispose();
    }

    private static void paintGlobe(Graphics2D g) {
        g.drawOval(3, 3, 30, 30);
        g.drawLine(3, 18, 33, 18);
        g.drawOval(11, 3, 14, 30);
        g.drawArc(3, 10, 30, 16, 0, 180);
        g.drawArc(3, 10, 30, 16, 180, 180);
    }

    private static void paintDashboard(Graphics2D g) {
        // Modern dashboard home icon
        Path2D p = new Path2D.Double();
        p.moveTo(18, 5);
        p.lineTo(6, 15);
        p.lineTo(6, 31);
        p.lineTo(13, 31);
        p.lineTo(13, 22);
        p.curveTo(13, 20, 23, 20, 23, 22);
        p.lineTo(23, 31);
        p.lineTo(30, 31);
        p.lineTo(30, 15);
        p.closePath();
        g.draw(p);
    }

    private static void paintClients(Graphics2D g) {
        // Two people outline
        g.drawOval(12, 6, 8, 8);
        g.drawArc(6, 17, 20, 15, 0, 180);
        g.drawOval(22, 9, 7, 7);
        g.drawArc(19, 19, 13, 13, 0, 150);
    }

    private static void paintScan(Graphics2D g) {
        // Magnifier with scanner target
        g.drawOval(6, 6, 17, 17);
        g.drawLine(19, 19, 31, 31);
        g.drawOval(11, 11, 7, 7);
    }

    private static void paintPing(Graphics2D g) {
        // Pulse / ECG heartbeat wave
        Path2D p = new Path2D.Double();
        p.moveTo(3, 19);
        p.lineTo(9, 19);
        p.lineTo(13, 9);
        p.lineTo(18, 29);
        p.lineTo(23, 13);
        p.lineTo(27, 21);
        p.lineTo(29, 19);
        p.lineTo(33, 19);
        g.draw(p);
    }

    private static void paintMonitoring(Graphics2D g) {
        // 3 vertical bars: short, medium, tall
        g.fillRoundRect(6, 20, 6, 12, 3, 3);
        g.fillRoundRect(15, 12, 6, 20, 3, 3);
        g.fillRoundRect(24, 6, 6, 26, 3, 3);
    }

    private static void paintScreen(Graphics2D g) {
        // Desktop display monitor
        g.drawRoundRect(4, 6, 28, 20, 3, 3);
        g.drawLine(18, 26, 18, 31);
        g.drawLine(11, 31, 25, 31);
    }

    private static void paintPower(Graphics2D g) {
        // Power button symbol: circle with opening and top vertical bar
        g.drawArc(7, 7, 22, 22, 50, 260);
        g.drawLine(18, 4, 18, 17);
    }

    private static void paintChat(Graphics2D g) {
        // Speech bubble with 3 dots
        Path2D p = new Path2D.Double();
        p.moveTo(6, 8);
        p.curveTo(6, 6, 8, 4, 11, 4);
        p.lineTo(25, 4);
        p.curveTo(28, 4, 30, 6, 30, 8);
        p.lineTo(30, 22);
        p.curveTo(30, 24, 28, 26, 25, 26);
        p.lineTo(14, 26);
        p.lineTo(8, 31);
        p.lineTo(8, 26);
        p.curveTo(6, 26, 6, 24, 6, 22);
        p.closePath();
        g.draw(p);
        g.fillOval(11, 13, 3, 3);
        g.fillOval(17, 13, 3, 3);
        g.fillOval(23, 13, 3, 3);
    }

    private static void paintSettings(Graphics2D g) {
        Area gear = new Area(new Ellipse2D.Double(7, 7, 22, 22));
        for (int i = 0; i < 8; i++) {
            Rectangle2D tooth = new Rectangle2D.Double(15, 2, 6, 10);
            AffineTransform rotation = AffineTransform.getRotateInstance(i * Math.PI / 4.0, 18, 18);
            gear.add(new Area(rotation.createTransformedShape(tooth)));
        }
        gear.subtract(new Area(new Ellipse2D.Double(13, 13, 10, 10)));
        g.draw(gear);
    }

    private static void paintAbout(Graphics2D g) {
        // Circle with 'i'
        g.drawOval(4, 4, 28, 28);
        g.drawLine(18, 16, 18, 25);
        g.drawLine(15, 25, 21, 25);
        g.drawLine(16, 16, 18, 16);
        g.fillOval(16, 10, 4, 4);
    }

    private static void paintClock(Graphics2D g) {
        g.drawOval(4, 4, 28, 28);
        g.drawLine(18, 8, 18, 18);
        g.drawLine(18, 18, 25, 18);
    }

    private static void paintRefresh(Graphics2D g) {
        g.drawArc(6, 6, 24, 24, 45, 275);
        Path2D arrow = new Path2D.Double();
        arrow.moveTo(28, 5);
        arrow.lineTo(29, 13);
        arrow.lineTo(21, 12);
        g.draw(arrow);
    }

    private static void paintDisconnect(Graphics2D g) {
        // Plug / broken link
        g.drawRoundRect(7, 11, 14, 14, 3, 3);
        g.drawLine(4, 15, 7, 15);
        g.drawLine(4, 21, 7, 21);
        g.drawLine(21, 18, 26, 18);
        g.drawLine(26, 12, 32, 24);
        g.drawLine(26, 24, 32, 12);
    }

    private static void paintWindows(Graphics2D g) {
        // Windows 4-tiles logo
        g.fillRect(5, 5, 12, 12);
        g.fillRect(19, 5, 12, 12);
        g.fillRect(5, 19, 12, 12);
        g.fillRect(19, 19, 12, 12);
    }

    private static void paintIdCard(Graphics2D g) {
        g.drawRoundRect(5, 7, 26, 22, 4, 4);
        g.drawOval(9, 12, 6, 6);
        g.drawLine(18, 13, 27, 13);
        g.drawLine(18, 18, 27, 18);
        g.drawLine(9, 23, 27, 23);
    }

    private static void paintCalendar(Graphics2D g) {
        g.drawRoundRect(5, 8, 26, 22, 3, 3);
        g.drawLine(5, 14, 31, 14);
        g.drawLine(10, 5, 10, 9);
        g.drawLine(26, 5, 26, 9);
        g.fillOval(10, 18, 3, 3);
        g.fillOval(17, 18, 3, 3);
        g.fillOval(24, 18, 3, 3);
        g.fillOval(10, 23, 3, 3);
        g.fillOval(17, 23, 3, 3);
        g.fillOval(24, 23, 3, 3);
    }

    private static void paintWifi(Graphics2D g) {
        // Concentric waves
        g.drawArc(5, 9, 26, 26, 45, 90);
        g.drawArc(10, 14, 16, 16, 45, 90);
        g.drawArc(14, 18, 8, 8, 45, 90);
        g.fillOval(16, 24, 4, 4);
    }

    private static void paintCpu(Graphics2D g) {
        // Processor chip
        g.drawRoundRect(9, 9, 18, 18, 2, 2);
        g.drawRect(13, 13, 10, 10);
        // Top and bottom pins
        g.drawLine(12, 5, 12, 9); g.drawLine(18, 5, 18, 9); g.drawLine(24, 5, 24, 9);
        g.drawLine(12, 27, 12, 31); g.drawLine(18, 27, 18, 31); g.drawLine(24, 27, 24, 31);
        // Left and right pins
        g.drawLine(5, 12, 9, 12); g.drawLine(5, 18, 9, 18); g.drawLine(5, 24, 9, 24);
        g.drawLine(27, 12, 31, 12); g.drawLine(27, 18, 31, 18); g.drawLine(27, 24, 31, 24);
    }

    private static void paintRam(Graphics2D g) {
        // RAM memory module
        g.drawRoundRect(4, 10, 28, 16, 2, 2);
        g.drawRect(8, 13, 5, 7);
        g.drawRect(16, 13, 5, 7);
        g.drawRect(24, 13, 5, 7);
        // Gold contacts at bottom
        for (int x = 6; x <= 30; x += 3) {
            g.drawLine(x, 26, x, 29);
        }
    }

    private static void paintLaptop(Graphics2D g) {
        // Modern laptop
        g.drawRoundRect(7, 7, 22, 16, 3, 3);
        g.drawRect(9, 9, 18, 12);
        // Base
        Path2D base = new Path2D.Double();
        base.moveTo(4, 27);
        base.lineTo(32, 27);
        base.lineTo(30, 29);
        base.lineTo(6, 29);
        base.closePath();
        g.draw(base);
    }

    private static void paintCheckCircle(Graphics2D g) {
        g.drawOval(4, 4, 28, 28);
        Path2D p = new Path2D.Double();
        p.moveTo(11, 18);
        p.lineTo(16, 23);
        p.lineTo(25, 13);
        g.draw(p);
    }

    private static void paintXCircle(Graphics2D g) {
        g.drawOval(4, 4, 28, 28);
        g.drawLine(12, 12, 24, 24);
        g.drawLine(24, 12, 12, 24);
    }

    private static void paintLayers(Graphics2D g) {
        // Stacked network topology / layers
        Path2D l1 = new Path2D.Double();
        l1.moveTo(18, 7); l1.lineTo(29, 13); l1.lineTo(18, 19); l1.lineTo(7, 13); l1.closePath();
        g.draw(l1);
        Path2D l2 = new Path2D.Double();
        l2.moveTo(7, 18); l2.lineTo(18, 24); l2.lineTo(29, 18);
        g.draw(l2);
        Path2D l3 = new Path2D.Double();
        l3.moveTo(7, 23); l3.lineTo(18, 29); l3.lineTo(29, 23);
        g.draw(l3);
    }

    private static void paintJava(Graphics2D g) {
        // Coffee cup with steam
        Path2D cup = new Path2D.Double();
        cup.moveTo(7, 14);
        cup.lineTo(23, 14);
        cup.curveTo(23, 24, 19, 28, 15, 28);
        cup.curveTo(11, 28, 7, 24, 7, 14);
        cup.closePath();
        g.draw(cup);
        // Handle
        g.drawArc(21, 15, 8, 8, 270, 180);
        // Saucer
        g.drawLine(5, 30, 25, 30);
        // Steam
        g.drawArc(10, 6, 4, 6, 270, 180);
        g.drawArc(16, 5, 4, 6, 90, 180);
    }

    private static void paintCode(Graphics2D g) {
        g.drawLine(14, 11, 7, 18);
        g.drawLine(7, 18, 14, 25);
        g.drawLine(22, 11, 29, 18);
        g.drawLine(29, 18, 22, 25);
    }

    private static void paintPlay(Graphics2D g) {
        Path2D p = new Path2D.Double();
        p.moveTo(13, 9);
        p.lineTo(27, 18);
        p.lineTo(13, 27);
        p.closePath();
        g.fill(p);
    }

    private static void paintCross(Graphics2D g) {
        g.drawLine(10, 10, 26, 26);
        g.drawLine(26, 10, 10, 26);
    }

    private static void paintServer(Graphics2D g) {
        for (int y : new int[]{5, 14, 23}) {
            g.drawRoundRect(5, y, 26, 7, 2, 2);
            g.fillOval(9, y + 2, 3, 3);
            g.drawLine(16, y + 3, 27, y + 3);
        }
    }

    private static void paintShield(Graphics2D g) {
        Path2D shield = new Path2D.Double();
        shield.moveTo(18, 4); shield.lineTo(30, 9); shield.lineTo(29, 20);
        shield.curveTo(28, 26, 23, 30, 18, 33);
        shield.curveTo(13, 30, 8, 26, 7, 20); shield.lineTo(6, 9); shield.closePath();
        g.draw(shield);
        g.drawLine(12, 18, 16, 22);
        g.drawLine(16, 22, 24, 13);
    }

    private static void paintCertificate(Graphics2D g) {
        g.drawOval(7, 4, 22, 22);
        g.drawOval(12, 9, 12, 12);
        g.drawLine(12, 24, 10, 32);
        g.drawLine(10, 32, 17, 28);
        g.drawLine(24, 24, 26, 32);
        g.drawLine(26, 32, 19, 28);
    }

    private static void paintKey(Graphics2D g) {
        g.drawOval(5, 7, 16, 16);
        g.drawLine(18, 20, 30, 32);
        g.drawLine(24, 26, 28, 22);
        g.drawLine(27, 29, 31, 25);
        g.fillOval(10, 12, 6, 6);
    }

    private static void paintLock(Graphics2D g) {
        g.drawRoundRect(8, 16, 20, 16, 2, 2);
        Path2D shackle = new Path2D.Double();
        shackle.moveTo(12, 16);
        shackle.lineTo(12, 12);
        shackle.curveTo(12, 4, 24, 4, 24, 12);
        shackle.lineTo(24, 16);
        g.draw(shackle);
        g.fillOval(16, 21, 4, 4);
        g.drawLine(18, 24, 18, 28);
    }

    private static void paintCopy(Graphics2D g) {
        g.drawRoundRect(11, 9, 17, 21, 3, 3);
        g.drawLine(15, 5, 7, 5); g.drawLine(7, 5, 7, 25); g.drawLine(7, 25, 10, 25);
        g.drawLine(15, 14, 24, 14); g.drawLine(15, 19, 24, 19);
    }

    private static void paintEyeOff(Graphics2D g) {
        Path2D eye = new Path2D.Double();
        eye.moveTo(4, 18); eye.curveTo(10, 9, 25, 8, 32, 18); eye.curveTo(26, 27, 12, 28, 4, 18);
        g.draw(eye);
        g.drawOval(14, 14, 8, 8);
        g.drawLine(5, 5, 31, 31);
    }

    private static void paintRadio(Graphics2D g) {
        g.fillOval(15, 4, 6, 6);
        g.drawLine(18, 10, 18, 31);
        g.drawLine(14, 31, 22, 31);
        Path2D leftInner = new Path2D.Double();
        leftInner.moveTo(12, 12);
        leftInner.curveTo(7, 15, 7, 21, 12, 24);
        g.draw(leftInner);
        Path2D rightInner = new Path2D.Double();
        rightInner.moveTo(24, 12);
        rightInner.curveTo(29, 15, 29, 21, 24, 24);
        g.draw(rightInner);
        Path2D leftOuter = new Path2D.Double();
        leftOuter.moveTo(7, 7);
        leftOuter.curveTo(0, 13, 0, 23, 7, 29);
        g.draw(leftOuter);
        Path2D rightOuter = new Path2D.Double();
        rightOuter.moveTo(29, 7);
        rightOuter.curveTo(36, 13, 36, 23, 29, 29);
        g.draw(rightOuter);
    }

    private static void paintEmptyScan(Graphics2D g) {
        // Large laptop with magnifying glass
        g.drawRoundRect(6, 6, 24, 18, 3, 3);
        g.drawRect(8, 8, 20, 14);
        g.drawLine(3, 26, 33, 26);
        g.drawOval(16, 12, 12, 12);
        g.drawLine(25, 21, 31, 27);
    }

    private static void paintEmptyPing(Graphics2D g) {
        // Large wifi signal waves
        g.drawArc(4, 8, 28, 28, 45, 90);
        g.drawArc(9, 13, 18, 18, 45, 90);
        g.drawArc(14, 18, 8, 8, 45, 90);
        g.fillOval(16, 24, 4, 4);
    }
}

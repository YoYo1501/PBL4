package vn.networkadmin.client.presentation.screenshare;

import javax.swing.Icon;
import java.awt.BasicStroke;
import java.awt.Color;
import java.awt.Component;
import java.awt.Graphics;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.geom.Arc2D;
import java.awt.geom.Ellipse2D;
import java.awt.geom.Line2D;
import java.awt.geom.Rectangle2D;
import java.awt.geom.RoundRectangle2D;

/** Font-independent vector icon set for the Swing client UI. */
public final class VectorIcon implements Icon {
    public enum Type { LOGO, DASHBOARD, CHAT, NETWORK, LOGS, SETTINGS, ABOUT, CONNECTION, SESSION, CLIENT, SCREEN, CONTROL, CALENDAR, TRAFFIC, PERFORMANCE, WORKSTATION, COMPUTER, OS, CPU, RAM, RESOLUTION, WIFI, MAC, SEND, STOP, LOCK, REVOKE, DOT, CLOCK }

    private final Type type;
    private final int size;
    private final Color color;

    public VectorIcon(Type type, int size, Color color) { this.type = type; this.size = Math.max(10, size); this.color = color; }
    @Override public int getIconWidth() { return size; }
    @Override public int getIconHeight() { return size; }

    @Override public void paintIcon(Component component, Graphics graphics, int x, int y) {
        Graphics2D g = (Graphics2D) graphics.create(); g.translate(x, y); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(color); g.setStroke(new BasicStroke(Math.max(1.4f, size / 11f), BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND)); IconCanvas c = new IconCanvas(g); float s = size;
        switch (type) {
            case LOGO, NETWORK -> globe(c, s);
            case DASHBOARD, CLIENT, COMPUTER, WORKSTATION -> monitor(c, s);
            case CHAT -> { c.round(s*.14f, s*.18f, s*.72f, s*.53f, s*.18f); c.line(s*.30f, s*.71f, s*.23f, s*.87f); c.line(s*.23f, s*.87f, s*.39f, s*.72f); dots(c, s); }
            case LOGS -> { c.round(s*.22f, s*.12f, s*.56f, s*.76f, s*.06f); c.line(s*.34f,s*.35f,s*.66f,s*.35f); c.line(s*.34f,s*.52f,s*.66f,s*.52f); c.line(s*.34f,s*.69f,s*.58f,s*.69f); }
            case SETTINGS -> { c.oval(s*.26f,s*.26f,s*.48f,s*.48f); for (int i=0;i<8;i++) { double a=i*Math.PI/4; c.line(s*.5f+(float)Math.cos(a)*s*.34f,s*.5f+(float)Math.sin(a)*s*.34f,s*.5f+(float)Math.cos(a)*s*.47f,s*.5f+(float)Math.sin(a)*s*.47f); } c.fillOval(s*.45f,s*.45f,s*.1f,s*.1f); }
            case ABOUT -> { c.oval(s*.18f,s*.18f,s*.64f,s*.64f); c.line(s*.5f,s*.43f,s*.5f,s*.68f); c.fillOval(s*.47f,s*.27f,s*.06f,s*.06f); }
            case CONNECTION, DOT -> c.fillOval(s*.25f,s*.25f,s*.5f,s*.5f);
            case SESSION, CALENDAR -> { c.round(s*.16f,s*.2f,s*.68f,s*.62f,s*.08f); c.line(s*.16f,s*.4f,s*.84f,s*.4f); c.line(s*.33f,s*.1f,s*.33f,s*.3f); c.line(s*.67f,s*.1f,s*.67f,s*.3f); }
            case SCREEN -> { c.round(s*.17f,s*.18f,s*.66f,s*.48f,s*.06f); c.line(s*.5f,s*.67f,s*.5f,s*.82f); c.line(s*.34f,s*.84f,s*.66f,s*.84f); }
            case CONTROL -> { c.line(s*.18f,s*.1f,s*.79f,s*.52f); c.line(s*.79f,s*.52f,s*.57f,s*.56f); c.line(s*.57f,s*.56f,s*.5f,s*.83f); c.line(s*.18f,s*.1f,s*.5f,s*.83f); }
            case TRAFFIC -> { c.line(s*.35f,s*.8f,s*.35f,s*.17f); c.line(s*.35f,s*.17f,s*.18f,s*.34f); c.line(s*.35f,s*.17f,s*.52f,s*.34f); c.line(s*.65f,s*.2f,s*.65f,s*.83f); c.line(s*.65f,s*.83f,s*.48f,s*.66f); c.line(s*.65f,s*.83f,s*.82f,s*.66f); }
            case PERFORMANCE -> { c.line(s*.18f,s*.78f,s*.18f,s*.5f); c.line(s*.4f,s*.78f,s*.4f,s*.3f); c.line(s*.62f,s*.78f,s*.62f,s*.16f); c.line(s*.84f,s*.78f,s*.84f,s*.42f); }
            case OS -> { c.rect(s*.18f,s*.18f,s*.64f,s*.64f); c.line(s*.5f,s*.18f,s*.5f,s*.82f); c.line(s*.18f,s*.5f,s*.82f,s*.5f); }
            case CPU -> { c.round(s*.25f,s*.25f,s*.5f,s*.5f,s*.08f); for(int i=0;i<4;i++){float p=s*(.3f+i*.13f);c.line(p,s*.12f,p,s*.25f);c.line(p,s*.75f,p,s*.88f);c.line(s*.12f,p,s*.25f,p);c.line(s*.75f,p,s*.88f,p);} }
            case RAM -> { c.round(s*.14f,s*.3f,s*.72f,s*.38f,s*.05f); for(int i=0;i<4;i++){float p=s*(.27f+i*.15f);c.line(p,s*.38f,p,s*.6f);c.line(p,s*.73f,p,s*.86f);} }
            case RESOLUTION -> { c.round(s*.12f,s*.18f,s*.76f,s*.55f,s*.08f); c.line(s*.5f,s*.73f,s*.5f,s*.85f); c.line(s*.32f,s*.86f,s*.68f,s*.86f); }
            case WIFI -> { c.arc(s*.15f,s*.2f,s*.7f,s*.52f,35,110); c.arc(s*.29f,s*.37f,s*.42f,s*.31f,35,110); c.fillOval(s*.46f,s*.7f,s*.08f,s*.08f); }
            case MAC -> { c.round(s*.18f,s*.25f,s*.64f,s*.5f,s*.08f); c.line(s*.3f,s*.25f,s*.3f,s*.75f); c.line(s*.5f,s*.25f,s*.5f,s*.75f); c.line(s*.7f,s*.25f,s*.7f,s*.75f); }
            case SEND -> { c.fillRound(s*.12f,s*.2f,s*.76f,s*.6f,s*.13f); g.setColor(Color.WHITE); c.line(s*.28f,s*.5f,s*.72f,s*.5f); c.line(s*.57f,s*.35f,s*.72f,s*.5f); c.line(s*.57f,s*.65f,s*.72f,s*.5f); }
            case STOP -> c.fillRect(s*.23f,s*.23f,s*.54f,s*.54f);
            case LOCK -> { c.round(s*.23f,s*.43f,s*.54f,s*.4f,s*.05f); c.arc(s*.34f,s*.16f,s*.32f,s*.45f,0,180); c.line(s*.5f,s*.58f,s*.5f,s*.68f); }
            case REVOKE -> { c.oval(s*.16f,s*.16f,s*.68f,s*.68f); c.line(s*.3f,s*.5f,s*.7f,s*.5f); }
            case CLOCK -> { c.oval(s*.15f,s*.15f,s*.7f,s*.7f); c.line(s*.5f,s*.5f,s*.5f,s*.3f); c.line(s*.5f,s*.5f,s*.67f,s*.6f); }
        }
        g.dispose();
    }

    private static void globe(IconCanvas c, float s) { c.oval(s*.12f,s*.12f,s*.76f,s*.76f); c.oval(s*.34f,s*.12f,s*.32f,s*.76f); c.line(s*.14f,s*.5f,s*.86f,s*.5f); c.arc(s*.2f,s*.25f,s*.6f,s*.5f,0,180); }
    private static void monitor(IconCanvas c, float s) { c.round(s*.13f,s*.15f,s*.74f,s*.52f,s*.06f); c.line(s*.5f,s*.67f,s*.5f,s*.83f); c.line(s*.31f,s*.85f,s*.69f,s*.85f); }
    private static void dots(IconCanvas c, float s) { c.fillOval(s*.33f,s*.42f,s*.08f,s*.08f); c.fillOval(s*.46f,s*.42f,s*.08f,s*.08f); c.fillOval(s*.59f,s*.42f,s*.08f,s*.08f); }

    private static final class IconCanvas {
        private final Graphics2D g;
        private IconCanvas(Graphics2D g) { this.g = g; }
        private void line(float x1,float y1,float x2,float y2){g.draw(new Line2D.Float(x1,y1,x2,y2));}
        private void rect(float x,float y,float w,float h){g.draw(new Rectangle2D.Float(x,y,w,h));}
        private void round(float x,float y,float w,float h,float r){g.draw(new RoundRectangle2D.Float(x,y,w,h,r,r));}
        private void oval(float x,float y,float w,float h){g.draw(new Ellipse2D.Float(x,y,w,h));}
        private void fillOval(float x,float y,float w,float h){g.fill(new Ellipse2D.Float(x,y,w,h));}
        private void fillRect(float x,float y,float w,float h){g.fill(new Rectangle2D.Float(x,y,w,h));}
        private void fillRound(float x,float y,float w,float h,float r){g.fill(new RoundRectangle2D.Float(x,y,w,h,r,r));}
        private void arc(float x,float y,float w,float h,float start,float extent){g.draw(new Arc2D.Float(x,y,w,h,start,extent,Arc2D.OPEN));}
    }
}

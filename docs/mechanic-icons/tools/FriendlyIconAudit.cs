using System;
using System.Drawing;
using System.Globalization;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class FriendlyIconAudit {
 public static string Inspect(string path) {
  using(var b = new Bitmap(path)) {
   var rect = new Rectangle(0,0,b.Width,b.Height);
   var data=b.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
   var px=new byte[data.Stride*b.Height]; Marshal.Copy(data.Scan0,px,0,px.Length); b.UnlockBits(data);
   int zero=0,opaque=0,partial=0,warm=0,minA=255,maxA=0,x0=b.Width,y0=b.Height,x1=-1,y1=-1,border=0;
   for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++) {
    int i=y*data.Stride+x*4, a=px[i+3]; minA=Math.Min(minA,a);maxA=Math.Max(maxA,a);
    if(a==0)zero++;else {if(a==255)opaque++;else partial++;x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);if(x==0||y==0||x==b.Width-1||y==b.Height-1)border++;}
    if(a>=128){double r=px[i+2]/255.0,g=px[i+1]/255.0,bl=px[i]/255.0;double mx=Math.Max(r,Math.Max(g,bl)),mn=Math.Min(r,Math.Min(g,bl)),delta=mx-mn;double hue=Color.FromArgb(px[i+2],px[i+1],px[i]).GetHue();if(mx>=.28&&delta/mx>=.35&&hue<=65)warm++;}
   }
   return string.Format(CultureInfo.InvariantCulture,"{{\"width\":{0},\"height\":{1},\"alphaMin\":{2},\"alphaMax\":{3},\"transparentPixels\":{4},\"opaquePixels\":{5},\"partialPixels\":{6},\"bounds\":[{7},{8},{9},{10}],\"borderPixels\":{11},\"saturatedWarmScreenPixels\":{12}}}",b.Width,b.Height,minA,maxA,zero,opaque,partial,x0,y0,x1+1,y1+1,border,warm);
  }
 }
}

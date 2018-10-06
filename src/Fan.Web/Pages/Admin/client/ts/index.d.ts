 import _vue from 'vue'
 import _axios from 'axios'
 import { Component, Emit, Prop, Watch } from 'vue-property-decorator';

 declare global {
     const Vue: typeof _vue;
     const axios: typeof _axios;
     const adminMixin: any;
 }